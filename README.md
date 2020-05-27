# WikipediaExport
[![MIT License](https://img.shields.io/github/license/wolfgarbe/wikipediaexport.png)]
========
Export from Wikipedia dump files to JSON or Text files

Sometimes we need a big text corpus for indexing & search testing, benchmarking and other information retrieval tasks.
The Wikipedia data is ideal because its is big (7 million documents in English Wikipedia) and available in many languages.

The only problem is that the XML format it comes with is somewhat proprietary and inaccessible. WikipediaExport tries to solve this problem by converting the XML dumpf to either plain text or JSON.

Download wikipedia dump files at: <br>
http://dumps.wikimedia.org/enwiki/latest/    
https://dumps.wikimedia.org/enwiki/latest/enwiki-latest-pages-articles.xml.bz2"

#### Usage 

Export to text file:
dotnet WikipediaExport.dll inputpath="C:\data\wikipedia/enwiki-latest-pages-articles.xml" format=text

Export to JSON file:
dotnet WikipediaExport.dll inputpath="C:\data\wikipedia/enwiki-latest-pages-articles.xml" format=json
