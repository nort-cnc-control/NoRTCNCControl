using System;
using Xunit;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlConnection;
using System.Threading;
using System.Collections.Generic;

namespace ControlConnection.Tests
{
    public class UnitTest1
    {

        [Fact]
        public void TestSend()
        {
            string message = "{\"value\":10}";
            string expected = "12;{\"value\":10};";
           
            var stream = new MemoryStream();
            var sender = new MessageSender(stream);
            sender.MessageSend(message);
            
            stream.Seek(0, SeekOrigin.Begin);
            StreamReader reader = new StreamReader( stream );
            string text = reader.ReadToEnd();
            Console.WriteLine("Result: {0}", text);
            Assert.Equal(expected, text);
        }

        [Fact]
        public void TestReceive_1()
        {
            EventWaitHandle rcved = new EventWaitHandle(false, EventResetMode.ManualReset);
            List<String> rcvd = new List<string>();
            void rcv(String str)
            {
                Console.WriteLine("Message Received: {0}", str);
                rcvd.Add(str);
                rcved.Set();
            }
            string message = "12;{\"value\":10};";
            string expected = "{\"value\":10}";
            
            var stream = new MemoryStream();
            var receiver = new MessageReceiver(stream);
            receiver.MessageReceived += rcv;
            
            byte[] msg = System.Text.Encoding.UTF8.GetBytes(message);
            stream.Write(msg, 0, msg.Length);
            stream.Seek(0, SeekOrigin.Begin);
            receiver.Run();

            rcved.WaitOne();
            
            receiver.Dispose();
            
            Assert.Equal(1, rcvd.Count);
            Assert.Equal(expected, rcvd[0]);
            
        }
    }
}
