using ClickLib.Clicks;
using Echokraut.DataClasses;

namespace Echokraut.Helper
{
    public unsafe static class ClickHelper
    {

        public static void Click()
        {
            ClickTalk.Using(AddonTalkHelper.Address).Click();
        }
    }
}
