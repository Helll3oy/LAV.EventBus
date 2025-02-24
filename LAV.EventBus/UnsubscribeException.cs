using System;
using System.Runtime.Serialization;

namespace LAV.EventBus
{
    [Serializable]
    internal class UnsubscribeException : Exception
    {
        public UnsubscribeException()
        {
        }

        public UnsubscribeException(string message) : base(message)
        {
        }

        public UnsubscribeException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected UnsubscribeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
