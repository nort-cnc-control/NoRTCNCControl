﻿using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Log;

namespace ControlConnection
{
    internal class MessageReader
    {
        public event Action<String> MessageReceived;

        private enum State
        {
            ReadingLength,
            ReadingData,
        }

        private State state;
        private String buffer;
        private int messageLen;

        public MessageReader()
        {
            state = State.ReadingLength;
            buffer = "";
        }

        private bool ParseLength()
        {
            if (buffer.Length == 0)
            {
                messageLen = -1;
                return false;
            }
            if (!Char.IsDigit(buffer[0]))
            {
                throw new ArgumentException("Buffer must be started with digit");
            }
            int i;
            String lenbuf = "";
            for (i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == ';')
                    break;
                else if (!Char.IsDigit(buffer[i]))
                    throw new ArgumentException("Buffer must match \\d+;data;");
                lenbuf += buffer[i];
            }
            if (i == buffer.Length)
            {
                messageLen = -1;
                return false;
            }
            messageLen = Int32.Parse(lenbuf);
            buffer = buffer.Substring(lenbuf.Length + 1);
            state = State.ReadingData;
            return true;
        }

        private bool ParseData()
        {
            if (buffer.Length < messageLen)
            {
                return false;
            }
            var data = buffer.Substring(0, messageLen);
            buffer = buffer.Substring(messageLen + 1);
            state = State.ReadingLength;
            MessageReceived?.Invoke(data);
            return true;
        }

        public void AddChunk(String data)
        {
            bool cnt;
            if (data.Length == 0)
                return;
            buffer += data;

            do
            {
                switch (state)
                {
                    case State.ReadingLength:
                    {
                        cnt = ParseLength();         
                        break;
                    }
                    case State.ReadingData:
                    {
                        cnt = ParseData();
                        break;
                    }
                    default:
                        throw new InvalidProgramException("Invalid state");
                }
            }
            while (cnt);
        }
    }

    public class MessageReceiver : IDisposable, ILoggerSource
    {
        public event Action<String> MessageReceived;
        
        private System.IO.Stream stream;

        private Thread streamReader;
        private MessageReader reader;

        private bool end;
        public string Name => "message receiver";
        private CancellationToken Token => tokenSource.Token;
        private CancellationTokenSource tokenSource;

        private void ReadingThread()
        {
            byte[] buffer = new byte[1000];
            while (true)
            {
                var task = stream.ReadAsync(buffer, 0, 1000);
                try
                {
                    task.Wait(Token);
                }
                catch
                {
                    break;
                }

                int len = task.Result;
                if (len > 0)
                {
                    end = false;
                    var chunk = System.Text.Encoding.UTF8.GetString(buffer, 0, len);
                    reader.AddChunk(chunk);
                }
                else
                {
                    Thread.Sleep(10);
                    end = true;
                    waiter.Set();
                    continue;
                }
            }
        }

        private List<String> receivedStrings;
        private EventWaitHandle waiter;

        public MessageReceiver(System.IO.Stream stream)
        {
            if (!stream.CanRead)
                throw new ArgumentException("Invalid read stream");
            reader = new MessageReader();
            reader.MessageReceived += (msg) => {
                MessageReceived?.Invoke(msg);
                OnReceive(msg);
            };

            this.stream = stream;
            streamReader = new Thread(new ThreadStart(ReadingThread));
            waiter = new EventWaitHandle(false, EventResetMode.ManualReset);
            receivedStrings = new List<string>();
            tokenSource = new CancellationTokenSource();
        }

        public void Run()
        {
            tokenSource = new CancellationTokenSource();
            end = false;
            streamReader.Start();
        }

        public void Stop()
        {
            tokenSource.Cancel();
            waiter.Set();
            streamReader.Join();
        }

        public void Dispose()
        {
            if (!Token.IsCancellationRequested)
                Stop();
        }

        private void OnReceive(String str)
        {
            Logger.Instance.Debug(this, "receive", str);
            receivedStrings.Add(str);
            waiter.Set();
        }

        public String MessageReceive()
        {
            if (receivedStrings.Count == 0)
            {
                waiter.Reset();
                waiter.WaitOne();
            }
            if (end || Token.IsCancellationRequested)
                return null;
            String rcvd = receivedStrings[0];
            receivedStrings.RemoveAt(0);
            return rcvd;
        }
    }
}
