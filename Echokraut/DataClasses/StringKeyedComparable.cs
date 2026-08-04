using System;

namespace Echokraut.DataClasses
{
    /// <summary>
    /// Base for small data classes whose identity is the case-insensitive string returned by
    /// <see cref="object.ToString"/>. Centralises the GetHashCode / Equals / CompareTo triad that
    /// was duplicated across <c>NpcMapData</c>, <c>EchokrautVoice</c>, <c>PhoneticCorrection</c>
    /// and <c>BackendVoiceItem</c>.
    ///
    /// Behaviour is preserved verbatim from the majority of those copies: hash via
    /// <c>ToLowerInvariant</c>, equality via <c>OrdinalIgnoreCase</c>, and a case-insensitive
    /// <b>descending</b> CompareTo (other vs this). Only instances of the exact same runtime type
    /// compare equal / orderable. Subclasses must override <see cref="object.ToString"/> to return
    /// their key, and may override <see cref="Equals"/> or <see cref="CompareTo"/> where their
    /// legacy form differed.
    /// </summary>
    public abstract class StringKeyedComparable : IComparable
    {
        public override int GetHashCode() => ToString()!.ToLowerInvariant().GetHashCode();

        public override bool Equals(object? obj)
            => obj is StringKeyedComparable other
               && GetType() == other.GetType()
               && ToString()!.Equals(other.ToString(), StringComparison.OrdinalIgnoreCase);

        public virtual int CompareTo(object? obj)
        {
            if (obj is not StringKeyedComparable other || GetType() != other.GetType())
                return -1;
            return other.ToString()!.ToLower().CompareTo(ToString()!.ToLower());
        }
    }
}
