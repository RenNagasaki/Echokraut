using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class PhoneticCorrection : StringKeyedComparable
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

        // Legacy: culture-ToLower equality (differs from the base's OrdinalIgnoreCase). Kept
        // verbatim — phonetic-correction text is user-entered and effectively ASCII/Latin, so the
        // two agree in practice, but we don't change persisted-dedup semantics here.
        public override bool Equals(object? obj)
        {
            return (obj as PhoneticCorrection)?.ToString().ToLower() == ToString().ToLower();
        }
    }
}
