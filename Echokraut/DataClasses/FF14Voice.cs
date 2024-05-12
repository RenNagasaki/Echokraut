using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class FF14Voice
    {
        public string voiceName { get; set; }
        public string voice { get; set; }
        public string gender { get; set; }
        public string race { get; set; }

        public decimal patchVersion { get; set; }

        public override string ToString()
        {
            return gender + "/" + race + "/" + voiceName + "@" + patchVersion.ToString().Replace(",", ".");
        }
        public override bool Equals(object obj)
        {
            var item = obj as FF14Voice;

            if (item == null)
            {
                return false;
            }

            return this.ToString().Equals(item.ToString());
        }
    }
}
