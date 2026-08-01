using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.Exceptions
{
    public class AlltalkFailedException : Exception
    {
        public HttpStatusCode StatusCode { get; }

        public AlltalkFailedException(HttpStatusCode status, string? message) : base(message)
        {
            StatusCode = status;
        }
    }
}
