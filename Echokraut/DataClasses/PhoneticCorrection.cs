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

        public override int GetHashCode() => ToString().ToLowerInvariant().GetHashCode();
        public override bool Equals(object? obj)
        {
            return (obj as PhoneticCorrection)?.ToString().ToLower() == ToString().ToLower();
        }

        public int CompareTo(object? obj)
        {
            var otherObj = obj as PhoneticCorrection;
            return otherObj?.ToString().ToLower().CompareTo(ToString().ToLower()) ?? -1;
        }
    }
}
