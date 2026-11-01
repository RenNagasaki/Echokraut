using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.DataClasses;

public class EKEventId : EchoEventId
{
    public EKEventId(int id, TextSource textSource) : base(id, textSource) { }
}
