using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class LogMessage
    {
        public DateTime timeStamp {  get; set; }
        public string message {  get; set; }
        public Vector4 color { get; set; }
        public EKEventId eventId { get; set; }
        public LogType type { get; set; }
    }
}
