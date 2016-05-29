using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PageExtractor
{
    public static class Utility
    {
        public static string GetPseudoBIDString()
        {
            const int Len = 11;
            Random r = new Random();
            string newId = Guid.NewGuid().ToString("N").Substring(0, Len);
            char[] newChars = new char[Len];
            int index = 0;
            foreach (char n in newId)
            {
                char t = n;
                if (Char.IsLetter(n) && r.Next(0, 2) == 1)
                    t = Char.ToUpper(n);
                newChars[index++] = t;
            }
            return new string(newChars);
        }

    }
}
