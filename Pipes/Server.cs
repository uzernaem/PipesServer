using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Pipes
{
    public partial class Server : Form
    {
        private IntPtr PipeHandle;
        private IntPtr ReturnPipeHandle;
        private Dictionary<string, IntPtr> ReturnPipes = new Dictionary<string, IntPtr>();
        private string PipeName = "\\\\" + System.Net.Dns.GetHostName() + "\\pipe\\ServerPipe";    // имя канала, Dns.GetHostName() - метод, возвращающий имя машины, на которой запущено приложение
        //private string ReturnPipeName = "\\\\" + System.Net.Dns.GetHostName() + "\\pipe\\ReturnServerPipe";    // имя канала, Dns.GetHostName() - метод, возвращающий имя машины, на которой запущено приложение
        private Thread t;                                                               // поток для обслуживания канала
        private bool _continue = true;                                                  // флаг, указывающий продолжается ли работа с каналом

        [DllImport("kernel32.dll")]
        static extern bool ConnectNamedPipe(IntPtr hNamedPipe,
            [In] ref NativeOverlapped lpOverlapped);

        // конструктор формы
        public Server()
        {
            InitializeComponent();

            // создание именованного канала
            PipeHandle = NamedPipeStream.CreateNamedPipe("\\\\.\\pipe\\ServerPipe", NamedPipeStream.PIPE_ACCESS_DUPLEX, NamedPipeStream.PIPE_TYPE_BYTE | NamedPipeStream.PIPE_WAIT, NamedPipeStream.PIPE_UNLIMITED_INSTANCES, 0, 1024, NamedPipeStream.NMPWAIT_WAIT_FOREVER, (IntPtr)0);
            //ReturnPipeHandle = NamedPipeStream.CreateNamedPipe("\\\\.\\pipe\\ReturnServerPipe", NamedPipeStream.PIPE_ACCESS_DUPLEX, NamedPipeStream.PIPE_TYPE_BYTE | NamedPipeStream.PIPE_WAIT, NamedPipeStream.PIPE_UNLIMITED_INSTANCES, 0, 1024, NamedPipeStream.NMPWAIT_WAIT_FOREVER, (IntPtr)0);
            //ReturnPipes.Add("test", ReturnPipeHandle);
            // вывод имени канала в заголовок формы, чтобы можно было его использовать для ввода имени в форме клиента, запущенного на другом вычислительном узле
            this.Text += ": " + PipeName;

            // создание потока, отвечающего за работу с каналом
            t = new Thread(Listen);
            //t2 = new Thread(SendMessage);
            t.Start();
            //t2.Start();
        }

        private void Listen()
        {

            uint realBytesReaded = 0;   // количество реально прочитанных из канала байтов
            uint BytesWritten = 0;

            // входим в бесконечный цикл работы с каналом
            while (_continue)
            {

                if (NamedPipeStream.ConnectNamedPipe(PipeHandle, (IntPtr)0))
                {
                    Thread t2 = new Thread(ReceiveMessage);
                    t2.Start();



                    // Thread.Sleep(500);                                                      // приостанавливаем работу потока перед тем, как приступить к обслуживанию очередного клиента
                }
            }
        }

        private void ReceiveMessage()
        {
            string msg = "";            // прочитанное сообщение
            //string user = "";
            uint realBytesReaded = 0;
            uint BytesWritten = 0;
            byte[] buff = new byte[1024];                                           // буфер прочитанных из канала байтов
            NamedPipeStream.FlushFileBuffers(PipeHandle);                                // "принудительная" запись данных, расположенные в буфере операционной системы, в файл именованного канала
            NamedPipeStream.ReadFile(PipeHandle, buff, 1024, ref realBytesReaded, (IntPtr)0);    // считываем последовательность байтов из канала в буфер buff                    
            msg = Encoding.Unicode.GetString(buff).Trim('\0');                                 // выполняем преобразование байтов в последовательность символов

            if (!Regex.IsMatch(msg, @" >>"))
            {
                if (!ReturnPipes.ContainsKey(msg))
                {
                    ReturnPipes.Add(msg, NamedPipeStream.CreateNamedPipe("\\\\.\\pipe\\" + msg, NamedPipeStream.PIPE_ACCESS_DUPLEX, NamedPipeStream.PIPE_TYPE_BYTE | NamedPipeStream.PIPE_WAIT, NamedPipeStream.PIPE_UNLIMITED_INSTANCES, 0, 1024, NamedPipeStream.NMPWAIT_WAIT_FOREVER, (IntPtr)0));
                    userList.Invoke((MethodInvoker)delegate
                    {
                        userList.Items.Add(msg);
                    });
                }
                else
                {
                    ReturnPipes.Remove(msg);
                    userList.Invoke((MethodInvoker)delegate
                    {
                        userList.Items.Remove(msg);
                    });
                }
            }

            else
            {
                if (msg != "")
                {
                    rtbMessages.Invoke((MethodInvoker)delegate
                    {
                        rtbMessages.Text += "\n >> " + msg;                             // выводим полученное сообщение на форму
                    });
                }

                string user = Regex.Match(msg, @"\w+(?= >>)").Value;

                //foreach (string key in ReturnPipes.Keys)
                //{
                //    Thread t3 = new Thread (new ParameterizedThreadStart(SendMessage));
                //    t3.Start(key);
                //}
                //{
                //if 
                foreach (string key in ReturnPipes.Keys)
                {
                    NamedPipeStream.ConnectNamedPipe(ReturnPipes[key], (IntPtr)0);
                    {

                        buff = Encoding.Unicode.GetBytes(msg);    // выполняем преобразование сообщения (вместе с идентификатором машины) в последовательность байт
                        ReturnPipeHandle = ReturnPipes[key];

                        NamedPipeStream.WriteFile(ReturnPipeHandle, buff, Convert.ToUInt32(buff.Length), ref BytesWritten, (IntPtr)0);         // выполняем запись последовательности байт в канал            
                        NamedPipeStream.FlushFileBuffers(ReturnPipeHandle);                                // "принудительная" запись данных, расположенные в буфере операционной системы, в файл именованного канала
                        NamedPipeStream.DisconnectNamedPipe(ReturnPipeHandle);                             // отключаемся от канала клиента 
                    }
                }
                //}
            }
            NamedPipeStream.DisconnectNamedPipe(PipeHandle);                             // отключаемся от канала клиента 
        }

        private void Server_FormClosing(object sender, FormClosingEventArgs e)
        {
            _continue = false;      // сообщаем, что работа с каналом завершена

            if (t != null)
                t.Abort();          // завершаем поток

            if ((int)PipeHandle != -1)
                NamedPipeStream.CloseHandle(PipeHandle);     // закрываем дескриптор канала

            foreach (string key in ReturnPipes.Keys)
            {
                if ((int)ReturnPipes[key] != -1)
                    NamedPipeStream.CloseHandle(ReturnPipes[key]);
            }
        }
    }
}
