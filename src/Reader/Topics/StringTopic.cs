using System;

namespace Reader.Topics
{
    public readonly struct StringTopic : IReaderTopic
    {
        public StringTopic(string value) => this.AsString = value;

        public string AsString { get; }

        public bool Equals(IReaderTopic other)
        {
            if (other is StringTopic stringTopic)
                return StringComparer.Ordinal.Equals(stringTopic.AsString, this.AsString);

            return false;
        }

        public override int GetHashCode() => this.AsString.GetHashCode();

        public override string ToString() => this.AsString;
    }
}