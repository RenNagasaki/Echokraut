using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class BackendData
    {
        public string BaseUrl;
        public string StreamPath { get; }
        public string ReadyPath { get; }
        public string VoicesPath { get; }
        public string StopPath { get; }
    }
}
