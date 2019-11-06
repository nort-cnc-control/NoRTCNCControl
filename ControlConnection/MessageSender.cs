using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ControlConnection
{
    public class MessageSender : IDisposable
    {
        private System.IO.Stream stream;
        private System.IO.BufferedStream buffered;
        public MessageSender(System.IO.Stream stream)
        {
            if (!stream.CanWrite)
                throw new ArgumentException("Invalid write stream");
            this.stream = stream;
            buffered = new System.IO.BufferedStream(this.stream);
        }

        public void MessageSend(String doc)
        {
            String lenstr = doc.Length.ToString();
            byte[] lenbuf = System.Text.Encoding.UTF8.GetBytes(lenstr);
            byte[] docbuf = System.Text.Encoding.UTF8.GetBytes(doc);
            buffered.Write(lenbuf, 0, lenbuf.Length);
            buffered.WriteByte((byte)';');
            buffered.Write(docbuf, 0, docbuf.Length);
            buffered.WriteByte((byte)';');
            buffered.Flush();
        }

        public void Dispose()
        {
        }
    }
}
