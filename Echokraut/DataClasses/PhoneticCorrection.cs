using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class PhoneticCorrection : IComparable
    {
        public string OriginalText = "";
        public string CorrectedText = "";

        public PhoneticCorrection(string originalText,  string correctedText)
        {
            this.OriginalText = originalText;
            this.CorrectedText = correctedText;
        }

        public override string ToString() {
            return $"{OriginalText} - {CorrectedText}";
        }

        public override bool Equals(object? obj)
        {
            if (((PhoneticCorrection)obj).ToString().ToLower() == ToString().ToLower())
                return true ;

            return false;
        }

        public int CompareTo(object? obj)
        {
            var otherObj = ((PhoneticCorrection)obj);
            return otherObj.ToString().ToLower().CompareTo(ToString().ToLower());
        }
    }
}
