WikipediaExport<br> 
[![MIT License](https://img.shields.io/github/license/wolfgarbe/wikipediaexport.png)](https://github.com/wolfgarbe/WikipediaExport/blob/master/LICENSE)
========
**Convert Wikipedia XML dump files to JSON or Text files**

Text corpora are required for algorithm design/benchmarking in information retrieval, machine learning, language processing.<br>
The Wikipedia data is ideal because it is large (7 million documents in English Wikipedia) and available in many languages.

Unfortunately the XML format of the Wikipedia dump is somewhat proprietary and inaccessible. WikipediaExport solves this problem by converting the XML dump to plain text or JSON - two formats that can be easily consumed by many tools.

Download wikipedia dump files at: <br>
http://dumps.wikimedia.org/enwiki/latest/    
https://dumps.wikimedia.org/enwiki/latest/enwiki-latest-pages-articles.xml.bz2

### Usage 

**Export to text file:**<br>
`dotnet WikipediaExport.dll inputpath="C:\data\wikipedia/enwiki-latest-pages-articles.xml" format=text`

**Export to JSON file:**<br>
`dotnet WikipediaExport.dll inputpath="C:\data\wikipedia/enwiki-latest-pages-articles.xml" format=json`

### Format output file 

**Text file**

Five consecutive lines constitute a single document:<br>
title<br>
content<br>
domain<br> 
url<br>
docDate ([Unix time](https://en.wikipedia.org/wiki/Unix_time): milliseconds since the beginning of 1970)<br>

**JSON file**

title<br>
content  (all "\r" have been replaced with " ")<br>
domain<br>
url<br>
docDate  ([Unix time](https://en.wikipedia.org/wiki/Unix_time): milliseconds since the beginning of 1970)<br>

### Application 

**WikipediaExport** is used to generate the input data for [LuceneBench](https://github.com/wolfgarbe/LuceneBench), a benchmark program to compare the performance of **Lucene** (a search engine library written in Java, powering the search platforms Solr and Elasticsearch) and **SeekStorm** (a high-performance search platform written in C#, powering the SeekStorm Search as a Service).

---

**WikipediaExport** is contributed by [**SeekStorm** - the high performance Search as a Service & search API](https://seekstorm.com)
