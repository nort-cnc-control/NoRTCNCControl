using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;

namespace ControlConnection
{
    public delegate bool StreamIsConnected(Stream stream);

    public class MessageSender : IDisposable
    {
        private Stream stream;

        public MessageSender(Stream stream)
        {
            if (!stream.CanWrite)
                throw new ArgumentException("Invalid write stream");
            this.stream = stream;
        }

        public void MessageSend(String doc)
        {
            String lenstr = doc.Length.ToString();
            String message = lenstr + ";" + doc + ";";
            byte[] buf = System.Text.Encoding.UTF8.GetBytes(message);
            //Console.WriteLine("Send RSP: {0}", message);
            stream.Write(buf, 0, buf.Length);
            stream.Flush();
        }

        public void Dispose()
        {
        }
    }
}
