using System;
using System.Collections.Generic;

namespace Extractor
{
    public class ExtractAST
    {
        public static List<String> ExtractASTFromText(string code)
        {
            Options opts = new Options();
            opts.MaxContexts = 2000;
            opts.MaxLength = 2000;
            opts.MaxWidth = 500;
            opts.MaxLength = 500;
            var extractor = new Extractor(code, opts);
            List<String> result = extractor.Extract();
            return result;
        }
    }
}
