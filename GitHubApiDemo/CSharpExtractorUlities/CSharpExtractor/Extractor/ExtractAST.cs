using CommandLine;
using CommandLine.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Extractor
{
    public class ExtractAST
    {
        public static List<String> ExtractASTFromText(string code, Options opts)
        {
            var extractor = new Extractor(code, opts);
            List<String> result = extractor.ExtractMethods();
            return result;
        }
    }
}
