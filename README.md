WikipediaExport<br> 
[![MIT License](https://img.shields.io/github/license/wolfgarbe/wikipediaexport.png)](https://github.com/wolfgarbe/WikipediaExport/blob/master/LICENSE)
========
Export from Wikipedia XML dump files to JSON or Text files

Sometimes a big text corpus is required for indexing & search testing, benchmarking and other information retrieval tasks.<br>
The Wikipedia data is ideal because it is large (7 million documents in English Wikipedia) and available in many languages.

The only problem is that the XML format it comes with is somewhat proprietary and inaccessible. WikipediaExport tries to solve this problem by converting the XML dumpf to either plain text or JSON.

Download wikipedia dump files at: <br>
http://dumps.wikimedia.org/enwiki/latest/    
https://dumps.wikimedia.org/enwiki/latest/enwiki-latest-pages-articles.xml.bz2

#### Usage 

**Export to text file:**<br>
dotnet WikipediaExport.dll inputpath="C:\data\wikipedia/enwiki-latest-pages-articles.xml" format=text

**Export to JSON file:**<br>
dotnet WikipediaExport.dll inputpath="C:\data\wikipedia/enwiki-latest-pages-articles.xml" format=json

#### Format output file 

**Text file**

Five consecutive lines constitute a single document: title, content, domain, url, date.

**JSON file**

string title<br>
string url<br>
string domain<br>
string content  (all "\r" have been replaced with " ")<br>
double docDate  ([Unix time](https://en.wikipedia.org/wiki/Unix_time): milliseconds since the beginning of 1970)<br>

#### Application 

**WikipediaExport** is used to generate the input data for [LuceneBench](https://github.com/wolfgarbe/LuceneBench), a benchmark program to compare the performance of **Lucene** (a search engine library written in Java, powering the search platforms Solr and Elasticsearch) and **SeekStorm** (a high-performance search platform written in C#, powering the SeekStorm Search as a Service).
