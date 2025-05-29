using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class EKEventId
    {
        public static int CurrentId { get; set; } = 1;
        public int Id { get; set; }
        public TextSource textSource { get; set; }

        public EKEventId(int id, TextSource textSource)
        {
            this.Id = id;
            this.textSource = textSource;
        }
    }
}
