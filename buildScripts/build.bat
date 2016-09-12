cd ..
mkdir DoubanSpider
move PageExtractor\PageExtractor\bin\Release\* DoubanSpider

"C:\Program Files\7-Zip\7z.exe" a DoubanSpider.zip DoubanSpider\*

mkdir D:\jenkins_home\Release\%BUILD_NUMBER%

COPY DoubanSpider.zip D:\jenkins_home\Release\%BUILD_NUMBER%

rmdir /s/q DoubanSpider

del DoubanSpider.zip