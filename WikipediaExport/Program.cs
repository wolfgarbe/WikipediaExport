using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;

namespace WikipediaExport
{
    class Program
    {
        public class LinkObject : IComparable
        {
            public string url;
            public string title;
            public bool isExternal = false;
            public bool isOfficial = false; //official, wikipedia, dmoz

            //for sort()
            int IComparable.CompareTo(object obj)
            {
                return url.CompareTo(((LinkObject)obj).url);
            }

            //for indexof()/contains()
            public override bool Equals(object obj)
            {
                return url.Equals(((LinkObject)obj).url);
            }
        }

        public static JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IgnoreNullValues = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static double DateToJsonDouble(DateTime date)
        {
            TimeSpan ts = (DateTime)date - DateTime.Parse("1/1/1970");
            return Math.Floor(ts.TotalMilliseconds);
        }

        public static DateTime TimeZoneToUtc(string s, string language)
        {
            DateTime publicationDate = DateTime.MinValue;
            //pages without date get minvalue, and never come into news
            //minvalue recognizable als invalid value
            if (String.IsNullOrEmpty(s)) return publicationDate;

            if ((s.Length == 21) && (s[10] == 'T') && (s[13] != ':')) s = s.Insert(13, ":");

            bool containsTimezone = false;
            try
            {
                containsTimezone = s.Contains("+") || (s.LastIndexOf("T") == s.Length - 1) || ((s.LastIndexOf("-") > s.Length - 7)) && (s.LastIndexOf("-") > 15);

                //Fri, 09 Mar 2012 19:29:43 +0000
                publicationDate = (DateTime.Parse(s.Replace("GMT+", "+").Replace("PST", "-8").Replace("PDT", "-7").Replace("MDT", "-6").Replace("EST", "-5").Replace("EDT", "-4").Replace("CTT", "+8").Replace("CST", "+8").Replace("UTC", "+0"))).ToUniversalTime();//.ToLocalTime();  //egal ob utc oder local, muss nur bei post genauso konvertiert werden

                //if not timezone, then create default local time depending on feed language
                if (!containsTimezone)
                {
                    if (language == "zh") publicationDate = publicationDate.AddHours(-8); else if (language == "de") publicationDate = publicationDate.AddHours(1); else publicationDate.AddHours(+7);
                }
            }
            catch (Exception e)
            {
                publicationDate = DateTime.MinValue;
            }

            //there was no internet before 1980
            if (publicationDate.Year < 1980) return DateTime.MinValue;
            //if date more than 7 days in future, then invalid date
            if (publicationDate.Subtract(DateTime.UtcNow).TotalDays > 7) return DateTime.MinValue;//bingo
            //we can not allow pages to be along time on top because of wrong date
            if (publicationDate > DateTime.UtcNow) return DateTime.UtcNow;//bingo
            return publicationDate;
        }

        public static string Url2domain(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                return uri.Host;
            }
            catch (Exception e)
            {
                Trace.WriteLine("Url2domain error: " + e.Message + " " + url);
                return "";
            }
        }

        public class DocObject
        {
            public string title;
            public string text;
            public string url;
            public string domain = "";
            public DateTime docDate;

            //processed properties after parsing
            public List<LinkObject> linkList = new List<LinkObject>();
            public static char sep = ((char)0);
            static string controlCharacters = @"[\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000b\u000c\u000e\u000F\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001F]";

            public static string CleanStringWikipedia(string content)
            {
                try
                {
                    if (string.IsNullOrEmpty(content)) return "";
                    //doppelte leerzeichen, $ - ( ) entfernen
                    content = Regex.Replace(content.Replace("|", " - ").Replace("--", "").Replace("\t", " ").Replace("\u00a0", " ").Replace("&nbsp;", " "), @"([\$\-\(\) ])\1+", "$1");
                    //remove multiple cr (n>=3) https://stackoverflow.com/questions/7780794/javascript-regex-remove-specific-consecutive-duplicate-characters
                    content = Regex.Replace(content.Replace("\r ", "\r").Replace("\r\n ", "\r").Replace("\r\n", "\r").Replace("\n ", "\r").Replace("\n", "\r").TrimStart('\r'), @"([\r])\1\1", "$1");
                    //remove control chars 
                    content = Regex.Replace(content, controlCharacters, ""); //a //d
                }
                catch (Exception e) { Trace.WriteLine(DateTime.UtcNow.ToString() + " " + "cleanstring: " + e.Message); }

                return content;
            }
        }

        public class Wikipedia
        {
            long wikipediaCount = 0;
            long wikipediaPosition = 0;
            string wikipediaTitle = "";

            public string StripRedirect(string url)
            {
                int i = url.LastIndexOf("http://");
                if (i > 0)
                {
                    url = url.Remove(0, i);
                    i = url.LastIndexOf("&");
                    if (i != -1)
                    {
                        url = url.Remove(i);
                    }
                }
                return url;
            }

            public string NormalizeHttp(string url)
            {
                if (url.Length > 5) return url.Substring(0, 5).ToLower() + url.Substring(5); else return url;
            }

            public string StripHash(string url)
            {
                try
                {
                    if (string.IsNullOrEmpty(url)) return url;

                    //strip forwarding
                    url = StripRedirect(url);

                    //golem rss
                    url = url.Replace("-rss.html", ".html");

                    //strip parameters if path contains date: only if date before parameter
                    string path; int i = url.IndexOf("?"); if (i != -1) path = url.Remove(i); else path = url;
                    if (path.Contains("/2010/") || path.Contains("/2011/") || path.Contains("/2012/"))
                    {
                        if (i != -1) { url = path; }
                    }

                    //amazon reference
                    i = url.IndexOf("/ref=");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("/from/atom10");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?kc=");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?cid=");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?source=");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?ftcamp");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?ITO=");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?fsrc=");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?partner");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?ref");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?csp=");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?src=");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?pk_campaign=");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?cm_mmc=");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?campaign_id=");
                    if (i != -1) { url = url.Remove(i); }

                    i = url.IndexOf("?track=");
                    if (i != -1) { url = url.Remove(i); }

                    //strip hash
                    i = url.IndexOf("#");
                    //if (i != -1) { url = url.Remove(0, i + 1);  }
                    if (i != -1) { url = url.Remove(i); }

                    //strip utm parameter (scienceblogs)
                    i = url.IndexOf("?utm_");
                    if (i != -1) { url = url.Remove(i); }

                    //strip utm parameter (news.cnet.com)
                    i = url.IndexOf("&utm_");
                    if (i != -1) { url = url.Remove(i); }

                    //strip part parameter (news.cnet.com)
                    i = url.IndexOf("?part=rss");
                    if (i != -1) { url = url.Remove(i); }

                    //strip utm parameter (allthingsd)
                    i = url.IndexOf("?mod=");
                    if (i != -1) { url = url.Remove(i); }

                    //strip awe.sm parameter
                    i = url.IndexOf("?awesm=");
                    if (i != -1) { url = url.Remove(i); }

                    //strip comments
                    i = url.IndexOf("comment-page-");
                    if (i != -1) { url = url.Remove(i); }

                    //strip autoplay
                    i = url.IndexOf("?autoplay=");
                    if (i != -1) { url = url.Remove(i); }

                    //strip rss
                    i = url.IndexOf("?rss");
                    if (i != -1) { url = url.Remove(i); }

                    //strip ? from end
                    url = url.TrimEnd(new char[] { '?' });

                    //canonisation/ canonical urls
                    //with / without index and slash
                    //index.htm / index.html / index.php, index.jsp, index.asp
                    if (url.EndsWith("index.html", StringComparison.OrdinalIgnoreCase)) url = url.Replace("index.html", "");
                    if (url.EndsWith("index.htm", StringComparison.OrdinalIgnoreCase)) url = url.Replace("index.htm", "");
                    if (url.EndsWith("index.php", StringComparison.OrdinalIgnoreCase)) url = url.Replace("index.php", "");
                    if (url.EndsWith("index.jsp", StringComparison.OrdinalIgnoreCase)) url = url.Replace("index.jsp", "");
                    if (url.EndsWith("index.asp", StringComparison.OrdinalIgnoreCase)) url = url.Replace("index.asp", "");


                    //normalization domain 
                    if (url.StartsWith("http") && (url.Length > 10))
                    {
                        int p1 = url.IndexOf("/", 10); // separator between domain and path
                        if (p1 != -1) url = url.Substring(0, p1).ToLower() + url.Substring(p1); else url = url.ToLower() + "/";//add a trailing slash, if none exists (http://test.com -> http://test.com/ )
                    }

                    return url;
                }
                catch (Exception e) { Trace.WriteLine(DateTime.UtcNow.ToString() + " " + "stripHash: " + e.Message + " " + url); return ""; }

            }

            public class WikipediaEntryObject
            {
                public string url;
                public string title;
                public string urlPrefix = "";
                public XElement el;
            }

            public Wikipedia()
            {

            }

            //1. opening tag: counter +- for nested tags
            public void StripTag(ref StringBuilder sb, string openingTag, string closingTag)
            {
                try
                {
                    int level = 0;
                    int p1 = -1;
                    for (int i = 0; i < sb.Length - closingTag.Length + 1; i++)
                    {
                        if (sb[i] == openingTag[0])
                        {
                            for (int j = 1; j < openingTag.Length; j++) if (sb[i + j] != openingTag[j]) goto skip;
                            level++;
                            if (level == 1)
                            {
                                p1 = i;
                            }
                            i += openingTag.Length - 1;//continue after tag
                        }
                        else if ((p1 != -1) && (sb[i] == closingTag[0]))
                        {
                            for (int j = 1; j < closingTag.Length; j++) if (sb[i + j] != closingTag[j]) goto skip;
                            level--;
                            if (level == 0)
                            {
                                sb.Remove(p1, i - p1 + closingTag.Length);
                                i = p1 - 1;
                                p1 = -1;
                            }
                            else
                            {
                                i += closingTag.Length - 1;//continue after tag
                            }
                        }
                    skip:;
                    }
                }
                catch (Exception e)
                {
                    Trace.WriteLine(DateTime.UtcNow.ToString() + " " + "StripTag: " + e.Message);
                }
            }

            public void StripTag4(ref StringBuilder sb, string openingTag, string closingTag)
            {
                Stack<int> start = new Stack<int>();
                int p1 = -1;
                for (int i = 0; i < sb.Length - closingTag.Length + 1; i++)
                {
                    if (sb[i] == openingTag[0])
                    {
                        for (int j = 1; j < openingTag.Length; j++) if (sb[i + j] != openingTag[j]) goto skip;
                        start.Push(i);
                        i += openingTag.Length - 1;//continue after tag
                    }
                    else if ((start.Count > 0) && (sb[i] == closingTag[0]))
                    {
                        for (int j = 1; j < closingTag.Length; j++) if (sb[i + j] != closingTag[j]) goto skip;

                        p1 = start.Pop();
                        int p2 = p1 + openingTag.Length - 1;//position second [
                        for (int j = i - 1; j > p1 + 1; j--) if (sb[j] == '|') { p2 = j; break; } //position of |
                        sb.Remove(i, closingTag.Length);// ]] remove
                        sb.Remove(p1, p2 - p1 + 1);//remove everything between [[ | 
                        //i: position first - remove length1 + remove length2 + closingTag.Length -1 ]
                        i -= (p2 - p1 + 2);//continue
                    }
                skip:;
                }
            }

            //===  ,variable length, including following spaces
            public void StripTag2(ref StringBuilder sb, string openingTag)
            {
                try
                {
                    for (int i = 0; i < sb.Length - openingTag.Length + 1; i++)
                    {
                        if (sb[i] == openingTag[0])
                        {
                            //required length
                            for (int j = 1; j < openingTag.Length; j++) if (sb[i + j] != openingTag[j]) goto skip;
                            int p2 = i + openingTag.Length - 1;
                            //variable length
                            while ((p2 + 1 < sb.Length) && (sb[p2 + 1] == openingTag[0])) p2++;
                            //trailing soaces
                            while ((p2 + 1 < sb.Length) && (sb[p2 + 1] == ' ')) p2++;
                            sb.Remove(i, p2 - i + 1);
                            i--;
                        }
                    skip:;
                    }
                }
                catch (Exception e)
                {
                    Trace.WriteLine(DateTime.UtcNow.ToString() + " " + "StripTag2: " + e.Message);
                }
            }


            // \n****  variable length, including following spaces
            public void StripTag3(ref StringBuilder sb, string openingTag)
            {
                try
                {
                    for (int i = 0; i < sb.Length - openingTag.Length; i++)
                    {
                        if (sb[i] == '\n')
                        {
                            //required length
                            for (int j = 0; j < openingTag.Length; j++) if (sb[i + j + 1] != openingTag[j]) goto skip;
                            int p2 = i + openingTag.Length;
                            //variable length
                            while ((p2 + 1 < sb.Length) && (sb[p2 + 1] == openingTag[0])) p2++;
                            //trailing soaces
                            while ((p2 + 1 < sb.Length) && (sb[p2 + 1] == ' ')) p2++;
                            sb.Remove(i + 1, p2 - i);
                            i--;
                        }
                    skip:;
                    }
                }
                catch (Exception e)
                {
                    Trace.WriteLine(DateTime.UtcNow.ToString() + " " + "StripTag3: " + e.Message);
                }

            }

            public void StripWikiTags(string text, ref DocObject doc, string wikipediaTitle)
            {
                if (String.IsNullOrEmpty(text)) return;
                try
                {
                    StringBuilder sb = new StringBuilder(text);

                    sb.Replace("'''", "");
                    sb.Replace("''", "");
                    StripTag4(ref sb, "[[", "]]");
                    StripTag(ref sb, "<!--", "-->");

                    ExtractOfficial(ref sb, ref doc.linkList, "{{dmoz|");
                    ExtractOfficial(ref sb, ref doc.linkList, "{{URL|");
                    ExtractOfficial(ref sb, ref doc.linkList, "{{Official website|");
                    ExtractLinks(ref sb, ref doc.linkList, wikipediaTitle);
                    ExtractVCite(ref sb, ref doc.linkList, wikipediaTitle);

                    StripTag(ref sb, "{|", "|}");
                    StripTag(ref sb, "{{", "}}");
                    StripTag(ref sb, "<", ">");
                    StripTag2(ref sb, "==");
                    StripTag3(ref sb, "*");
                    StripTag3(ref sb, "#");

                    doc.text = DocObject.CleanStringWikipedia(sb.ToString());
                }
                catch (Exception e)
                {
                    Trace.WriteLine(DateTime.UtcNow.ToString() + " " + "StripWikiTags: " + e.Message);
                }
            }


            public void ExtractLinks(ref StringBuilder sb, ref List<LinkObject> linkList, string wikipediaTitle)
            {
                try
                {
                    string openingTag = "[http";
                    string closingTag = "]";

                    int p1 = -1;
                    for (int i = 0; i < sb.Length - closingTag.Length + 1; i++)
                    {
                        if ((p1 == -1) && (sb[i] == openingTag[0]))
                        {
                            for (int j = 1; j < openingTag.Length; j++) if (sb[i + j] != openingTag[j]) goto skip;
                            p1 = i;
                        }
                        else if ((p1 != -1) && (sb[i] == closingTag[0]))
                        {
                            for (int j = 1; j < closingTag.Length; j++) if (sb[i + j] != closingTag[j]) goto skip;
                            string urltitle = sb.ToString(p1 + 1, i - p1 - 1).Trim();

                            LinkObject link = new LinkObject();
                            //extract title, if available
                            int p2 = urltitle.IndexOf(" ");
                            if (p2 == -1)
                            {
                                //strip hash
                                link.url = StripHash(urltitle).Trim();
                                link.title = "";
                            }
                            else
                            {
                                //strip hash
                                link.url = StripHash(urltitle.Substring(0, p2)).Trim();
                                link.title = urltitle.Substring(p2 + 1).Trim();
                            }
                            //prevent double
                            if (!linkList.Contains(link))
                            {
                                //contains wikipedia title in domain
                                if (link.url.IndexOf("." + wikipediaTitle, StringComparison.OrdinalIgnoreCase) != -1) link.isOfficial = true;
                                link.isExternal = true;
                                linkList.Add(link);
                            }

                            i = i + closingTag.Length;

                            p1 = -1;
                        }
                    skip:;
                    }
                }
                catch (Exception e)
                {
                    Trace.WriteLine(DateTime.UtcNow.ToString() + " " + "ExtractLinks error: " + e.Message + " #" + sb.ToString() + "#");
                }
            }

            public void ExtractOfficial(ref StringBuilder sb, ref List<LinkObject> linkList, string openingTag)
            {
                try
                {
                    string closingTag = "}}";

                    int p1 = -1;
                    for (int i = 0; i < sb.Length - closingTag.Length + 1; i++)
                    {
                        if ((p1 == -1) && (sb[i] == openingTag[0]))
                        {
                            for (int j = 1; j < openingTag.Length; j++) if (sb[i + j] != openingTag[j]) goto skip;
                            p1 = i;
                        }
                        else if ((p1 != -1) && (sb[i] == closingTag[0]))
                        {
                            for (int j = 1; j < closingTag.Length; j++) if (sb[i + j] != closingTag[j]) goto skip;
                            string urltitle = sb.ToString(p1 + openingTag.Length, i - p1 - openingTag.Length).Trim();

                            LinkObject link = new LinkObject();
                            //extract title, if available
                            int p2 = urltitle.IndexOf("|");
                            if (p2 != -1)
                            {
                                urltitle = urltitle.Remove(p2);
                            }
                            p2 = urltitle.IndexOf("l=");
                            if (p2 != -1)
                            {
                                urltitle = urltitle.Substring(2);
                            }
                            link.title = "Official homepage";
                            if (!urltitle.StartsWith("http")) { if (openingTag.Contains("dmoz")) { link.title = urltitle.Replace("/", ": ").Replace("_", " ") + " - Open Directory"; urltitle = "http://www.dmoz.org/" + urltitle; } else urltitle = "http://" + urltitle; }
                            //strip hash
                            link.url = StripHash(urltitle).Trim();


                            //prevent double
                            if (!linkList.Contains(link))
                            {
                                //internal links need to be extracted differently
                                link.isOfficial = true;
                                link.isExternal = true;
                                linkList.Add(link);
                            }

                            return;
                        }
                    skip:;
                    }
                }
                catch (Exception e)
                {
                    Trace.WriteLine(DateTime.UtcNow.ToString() + " " + "ExtractOfficial: " + e.Message);
                }
            }


            public void ExtractVCite(ref StringBuilder sb, ref List<LinkObject> linkList, string wikipediaTitle)
            {
                try
                {
                    string openingTag1 = "{{cite";
                    string openingTag2 = "{{vcite";
                    string closingTag = "}}";

                    int p1 = -1;
                    int l1 = 0;
                    for (int i = 0; i < sb.Length - closingTag.Length + 1; i++)
                    {

                        if ((p1 == -1) && (sb[i] == openingTag1[0])) //1. char is the same on tag1 and tag2
                        {
                            for (int j = 1; j < openingTag1.Length; j++) if (sb[i + j] != openingTag1[j])
                                {
                                    goto next;
                                }
                            l1 = 7;
                            goto noskip;
                        next:
                            for (int j = 1; j < openingTag2.Length; j++) if (sb[i + j] != openingTag2[j])
                                {
                                    goto skip;
                                }
                            l1 = 8;
                        noskip: p1 = i;

                        }
                        else if ((p1 != -1) && (sb[i] == closingTag[0]))
                        {
                            for (int j = 1; j < closingTag.Length; j++) if (sb[i + j] != closingTag[j]) goto skip;

                            //content between opening and closing tag
                            string urltitle = sb.ToString(p1 + l1, i - p1 - l1).Trim();

                            LinkObject link = new LinkObject();
                            //extract title, if available
                            int p2 = urltitle.IndexOf("|url=");
                            if (p2 != -1)
                            {
                                int p3 = urltitle.IndexOf("|", p2 + 1);
                                if (p3 != -1)
                                {
                                    //strip hash
                                    link.url = StripHash(urltitle.Substring(p2 + 5, p3 - p2 - 5)).Trim();
                                }
                            }

                            p2 = urltitle.IndexOf("|doi=");
                            if (p2 != -1)
                            {
                                int p3 = urltitle.IndexOf("|", p2 + 1);
                                if (p3 != -1)
                                {
                                    //strip hash
                                    if (String.IsNullOrEmpty(link.url)) link.url = "http://dx.doi.org/" + StripHash(urltitle.Substring(p2 + 5, p3 - p2 - 5)).Trim();
                                }
                            }

                            p2 = urltitle.IndexOf("|pmid=");
                            if (p2 != -1)
                            {
                                int p3 = urltitle.IndexOf("|", p2 + 1);
                                if (p3 != -1)
                                {
                                    //strip hash
                                    if (String.IsNullOrEmpty(link.url)) link.url = "http://www.ncbi.nlm.nih.gov/pubmed/" + StripHash(urltitle.Substring(p2 + 6, p3 - p2 - 6)).Trim() + "?dopt=Abstract";
                                }
                            }

                            p2 = urltitle.IndexOf("|pmc=");
                            if (p2 != -1)
                            {
                                int p3 = urltitle.IndexOf("|", p2 + 1);
                                if (p3 != -1)
                                {
                                    //strip hash
                                    if (String.IsNullOrEmpty(link.url)) link.url = "http://www.ncbi.nlm.nih.gov/pmc/articles/PMC" + StripHash(urltitle.Substring(p2 + 5, p3 - p2 - 5)).Trim() + "/";
                                }
                            }

                            int p4 = urltitle.IndexOf("|title=");
                            if (p4 != -1)
                            {

                                int p5 = urltitle.IndexOf("|", p4 + 1);
                                if (p5 != -1)
                                {
                                    link.title = urltitle.Substring(p4 + 7, p5 - p4 - 7).Trim();
                                }
                            }

                            //prevent double
                            if (!String.IsNullOrEmpty(link.url) && !linkList.Contains(link))
                            {
                                //contains wikipedia title in domain
                                if (link.url.IndexOf("." + wikipediaTitle, StringComparison.OrdinalIgnoreCase) != -1) link.isOfficial = true;
                                link.isExternal = true;
                                linkList.Add(link);
                            }

                            i = i + closingTag.Length;
                            p1 = -1;
                        }
                    skip:;
                    }
                }
                catch (Exception e)
                {
                    Trace.WriteLine(DateTime.UtcNow.ToString() + " " + "ExtractVCite: " + e.Message + " #" + sb.ToString() + "#");
                }
            }

            public DocObject CrawlWikipediaEntry(object WikipediaEntry)
            {
                try
                {
                    WikipediaEntryObject we = (WikipediaEntryObject)WikipediaEntry;

                    string text;
                    DateTime date;
                    XElement el3 = we.el.Element("{" + we.el.Name.Namespace + "}revision");
                    if (el3 != null)
                    {
                        XElement el4 = el3.Element("{" + we.el.Name.Namespace + "}timestamp");
                        if (el4 != null) date = TimeZoneToUtc(el4.Value, "en"); else date = DateTime.UtcNow;

                        XElement el5 = el3.Element("{" + we.el.Name.Namespace + "}text");
                        if (el5 != null) text = el5.Value; else text = "";
                    }
                    else
                    {
                        date = DateTime.UtcNow;
                        text = "";
                    }

                    DocObject doc = new DocObject();

                    //internal links
                    //external links
                    //image links

                    StripWikiTags(text, ref doc, we.title);

                    doc.url = we.url;
                    //title rewriting
                    doc.title = we.title + " - Wikipedia"; ;
                    doc.docDate = date;
                    doc.domain = Url2domain(doc.url);

                    return doc;
                }
                catch (Exception e9) { Trace.WriteLine(DateTime.UtcNow.ToString() + " " + "CrawlWikipediaEntry: " + e9.Message); return null; }

            }

            public class DocJson
            {
                public string title { get; set; }
                public string url { get; set; }
                public string domain { get; set; }
                public string content { get; set; }
                public double docDate { get; set; }
            }

            public byte[] openingBracketByte = Encoding.UTF8.GetBytes("[");
            public byte[] closingBracketByte = Encoding.UTF8.GetBytes("]");
            public byte[] commaByte = Encoding.UTF8.GetBytes(",");

            public void ReadWikipedia(object param)
            {
                // index = true:  index
                // index = false: export
                (string inputPath, string outputPath, string format, string urlPrefix) parameter = ((string inputPath, string outputPath, string format, string urlPrefix))param;

                if (!File.Exists(parameter.inputPath))
                {
                    Console.WriteLine("Wikipedia dump not found: " + parameter.inputPath);
                    return;
                }
                else Console.WriteLine("Wikipedia export started: "+ parameter.inputPath+" -> "+ parameter.outputPath+" ...");

                long size = 0;
                long count = 0;
                string title = "";
                bool skip = false;
                bool isText = (parameter.format == "text");

                using (FileStream outputFileStream = File.Create(parameter.outputPath))
                {
                    if (!isText) outputFileStream.Write(openingBracketByte);

                    using (FileStream inputFileStream = new FileStream(parameter.inputPath, FileMode.Open))
                    {
                        using (var reader = XmlReader.Create(inputFileStream))
                        {
                            size = inputFileStream.Length;

                            //continue
                            if (wikipediaCount > count)
                            {
                                count = wikipediaCount;

                                //start before, and skip until last title reached
                                if (wikipediaPosition > 10000000) { wikipediaPosition -= 10000000; skip = true; }
                                else
                                if (wikipediaPosition > 1000000) { wikipediaPosition -= 1000000; skip = true; }

                                inputFileStream.Seek(wikipediaPosition, SeekOrigin.Begin);
                            }


                            while (reader.Read())
                            {
                                try
                                {
                                    if (reader.NodeType == XmlNodeType.Element)
                                    {
                                        if (reader.Name == "page")
                                        {

                                            WikipediaEntryObject WikipediaEntry = new WikipediaEntryObject
                                            {
                                                el = XNode.ReadFrom(reader) as XElement
                                            };

                                            if (WikipediaEntry.el != null)
                                            {

                                                //-----------------

                                                string redirectTitle = "";
                                                title = WikipediaEntry.el.Element("{" + WikipediaEntry.el.Name.Namespace + "}title").Value;
                                                try
                                                {
                                                    XElement redirectElement = WikipediaEntry.el.Element("{" + WikipediaEntry.el.Name.Namespace + "}redirect");
                                                    if (redirectElement != null) redirectTitle = redirectElement.FirstAttribute.Value;
                                                }
                                                catch (Exception e) { Trace.WriteLine(DateTime.UtcNow.ToString() + " " + "title exception: " + e.Message); }


                                                //no internal wiki pages, no forwarding
                                                if (!title.Contains(":") && String.IsNullOrEmpty(redirectTitle))
                                                {
                                                    if (skip)
                                                    {
                                                        if (title == wikipediaTitle) skip = false;
                                                        continue;
                                                    }

                                                    WikipediaEntry.url = "http://" + parameter.urlPrefix + ".wikipedia.org/wiki/" + Uri.EscapeUriString(title.Replace(" ", "_"));
                                                    WikipediaEntry.urlPrefix = parameter.urlPrefix;

                                                    count++;

                                                    //urlprefix, title, el
                                                    WikipediaEntry.title = title;
                                                    DocObject doc = CrawlWikipediaEntry(WikipediaEntry);
                                                    if (doc != null)
                                                    {
                                                        if (isText)
                                                        {                          
                                                            byte[] info = Encoding.UTF8.GetBytes(
                                                                doc.url + Environment.NewLine+ 
                                                                doc.domain+ Environment.NewLine +
                                                                DateToJsonDouble(doc.docDate).ToString() + Environment.NewLine +
                                                                doc.title + Environment.NewLine +
                                                                doc.text.Replace("\r", " ") + Environment.NewLine
                                                                );
                                                            outputFileStream.Write(info, 0, info.Length);
                                                        }
                                                        else
                                                        { 
                                                            Wikipedia.DocJson docJson = new Wikipedia.DocJson
                                                            {
                                                                url = doc.url,
                                                                domain = doc.domain,
                                                                title = doc.title,
                                                                content = doc.text,
                                                                docDate = DateToJsonDouble(doc.docDate)
                                                            };
                                                            if (count > 1) outputFileStream.Write(commaByte);
                                                            outputFileStream.Write(JsonSerializer.SerializeToUtf8Bytes(docJson, jsonSerializerOptions));
                                                        }

                                                        if ((count % 100000) == 0) Console.WriteLine("docs: " + count.ToString("N0"));
                                                    }
                                                }

                                                //---
                                            }
                                        }
                                    }
                                }
                                catch (Exception e)
                                {
                                    Trace.WriteLine(DateTime.UtcNow.ToString() + " " + "wikipedia exception: " + e.Message);
                                }
                            }
                        }
                    }

                    if (!isText) outputFileStream.Write(closingBracketByte);

                }//end of AppendText
                Console.WriteLine("Wikipedia export finished:   docs: " + count.ToString("N0"));
            }
        }

        //txt/json export noch implementieren

        static void Main(string[] args)
        {
            //inputpath default: enwiki-latest-pages-articles.xml 
            string inputPath = "enwiki-latest-pages-articles.xml";
            //outputpath default: enwiki-latest-pages-articles.txt
            string outputPath = "";
            //format default: text
            string format = "text";

            if ((args.Length == 0) && !File.Exists(inputPath))
            {
                Console.WriteLine("Download wikipedia dump files at: http://dumps.wikimedia.org/enwiki/latest/    https://dumps.wikimedia.org/enwiki/latest/enwiki-latest-pages-articles.xml.bz2");
                Console.WriteLine("");
                Console.WriteLine("Commandline Parameter missing");
                Console.WriteLine("inputpath={path of wikipedia dump} outputpath={path of export file} format=text|json");
                Console.ReadKey();
                return;
            }

            foreach (string s in args)
            {
                string[] parameter = s.Split("=");
                if (parameter.Length > 1) switch (parameter[0])
                    {
                        case "inputpath":
                            if (File.Exists(parameter[1]))
                            {
                                inputPath = Path.GetFullPath(parameter[1]);
                                Console.WriteLine("inputpath=" + inputPath);
                            }
                            else Console.WriteLine("Invalid value: " + s);
                            break;
                        case "outputpath":
                            if (Directory.Exists(parameter[1]))
                            {
                                outputPath = Path.GetFullPath(parameter[1]);
                                Console.WriteLine("outputpath=" + outputPath);
                            }
                            else Console.WriteLine("Invalid value: " + s);
                            break;
                        case "format": //text or json
                            format = parameter[1].ToLower();
                            if ((format=="text")|| (format == "json"))
                            {
                                Console.WriteLine("format=" + format);
                            }
                            else Console.WriteLine("Invalid value: " + s);
                            break;    
                        default:
                            Console.WriteLine("Invalid parameter: " + s);
                            break;
                    }
            }

            if (!String.IsNullOrEmpty(inputPath) && String.IsNullOrEmpty(outputPath))
            {
                if (format=="text")
                    outputPath = Path.Combine( Path.GetDirectoryName(inputPath),Path.GetFileNameWithoutExtension(inputPath) + ".txt");
                else
                    outputPath = Path.Combine(Path.GetDirectoryName(inputPath), Path.GetFileNameWithoutExtension(inputPath) + ".json");
            }
            string urlPrefix = Path.GetFileName(inputPath).Substring(0, 1); //en

            Wikipedia wikipedia = new Wikipedia();
            wikipedia.ReadWikipedia((
                inputPath: inputPath,
                outputPath: outputPath,
                format: format,
                urlPrefix: urlPrefix)
            );
        }
    }
}
