using Echokraut.DataClasses;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.Helper
{
    public static class RomanNumeralsHelper
    {
        public static int RomanNumeralsToInt(string number, EKEventId eventId)
        {
            LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Replacing Roman: {number}", eventId);
            number = number.ToUpper();
            var result = 0;

            foreach (var letter in number)
            {
                result += ConvertRomanNumeralToNumber(letter);
            }

            if (number.Contains("IV") || number.Contains("IX"))
                result -= 2;

            if (number.Contains("XL") || number.Contains("XC"))
                result -= 20;

            if (number.Contains("CD") || number.Contains("CM"))
                result -= 200;

            LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Replaced Roman: {result}", eventId);
            return result;
        }

        private static int ConvertRomanNumeralToNumber(char letter)
        {
            switch (letter)
            {
                case 'M':
                    {
                        return 1000;
                    }

                case 'D':
                    {
                        return 500;
                    }

                case 'C':
                    {
                        return 100;
                    }

                case 'L':
                    {
                        return 50;
                    }

                case 'X':
                    {
                        return 10;
                    }

                case 'V':
                    {
                        return 5;
                    }

                case 'I':
                    {
                        return 1;
                    }

                default:
                    {
                        throw new ArgumentException("Ivalid charakter");
                    }
            }
        }
    }
}
