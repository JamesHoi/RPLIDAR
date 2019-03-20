using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.Ports;
using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;   

namespace RPLIDAR
{
    public partial class Form1 : Form
    {
        #region -- Load value --
        Thread read;
        Thread calcirclethread;

        SerialPort comm = new SerialPort();
        int showfre = 31;
        ScanPoint[,] ScanData;

        Byte[] STARTSCAN = new Byte[9] { 0xA5, 0x82, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x22 };
        Byte[] STOPSCAN = new Byte[2] { 0xA5, 0x25};
        Byte[] STARTROTATE = new Byte[6] { 0xA5, 0xF0, 0x02, 0xC4, 0x02, 0x91};
        Byte[] STOPROTATE = new Byte[6] { 0xA5, 0xF0, 0x02, 0x00, 0x00, 0x57 };
        string input = "";

        int loop = 0;

        string angle = "";
        string lastangle = "";
        int startalready = 0;

        int startangle = 0;
        bool StartOfNewScan = true;
        int typei = 0;
        double typed = 0;

        readonly string[] HealtStatusStrings = { "Good", "Poor", "Critical", "Unknown" };

        Bitmap m_bmp;
                                    //画布中的图像
        Point m_ptCanvas;           //画布原点在设备上的坐标
        Point m_ptBmp;              //图像位于画布坐标系中的坐标
        float m_nScale = 1.0F;      //缩放比例

        Point m_ptMouseMove;        //鼠标移动是在设备坐标上的坐标
        int showangle = 0;
        bool startshow = false;
        int oldangle = 0;
        float olddistance = 0;

        bool sendalready = false;
        bool startscan = false;
        int length = 7;
        int checklen = 0;
        byte[] buffer;
        int errornumber = 1;
        bool checkalready = false;
        string[,] lastdeg = new string[2, 16];
        int group = 0;
        static string port = "COM5";
        Stopwatch watch = new Stopwatch();
        double deltatime = 0;
        bool DRAWCIRCLE = false;

        Circle nicecircle;
        int[,] recordx = new int[32,31];
        int[,] recordy = new int[32,31];

        public Form1()
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterScreen;
            this.pictureBox1.BorderStyle = BorderStyle.FixedSingle;
            this.pictureBox1.MouseWheel += new MouseEventHandler(pictureBox1_MouseWheel);
        }

        //Form Load
        private void Form1_Load(object sender, EventArgs e)
        {
            read = new Thread(new ThreadStart(readvalue));
            read.IsBackground = true;
            calcirclethread = new Thread(new ThreadStart(circlethread));
            calcirclethread.IsBackground = true;
            m_bmp = new Bitmap(pictureBox1.Width, pictureBox1.Height);
            m_ptCanvas = new Point(pictureBox1.Width / 2, pictureBox1.Height / 2);
            m_ptBmp = new Point(-(m_bmp.Width / 2), -(m_bmp.Height / 2));
            ScanData = new ScanPoint[32, showfre+1];
            //if (comm.IsOpen)
            //{
            //    comm.Write(STOPROTATE, 0, (int)STOPROTATE.LongLength);
            //    comm.Write(STOPSCAN, 0, (int)STOPSCAN.LongLength);
            //}
            //else
            //{
            //    comm = new SerialPort(port, 115200, Parity.None, 8, StopBits.One);
            //    try
            //    {
            //        comm.Open();
            //        comm.Write(STOPROTATE, 0, (int)STOPROTATE.LongLength);
            //        comm.Write(STOPSCAN, 0, (int)STOPSCAN.LongLength);
            //        comm.Close();
            //    }
            //    catch { }
            //}
            //Thread.Sleep(1000);
        }


        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (comm.IsOpen)
                    comm.Write(STOPROTATE, 0, (int)STOPROTATE.LongLength);
                else
                {
                    comm = new SerialPort(port, 115200, Parity.None, 8, StopBits.One);
                    comm.Open();
                    comm.Write(STOPROTATE, 0, (int)STOPROTATE.LongLength);
                    comm.Close();
                    comm.Dispose();
                }
            }
            catch { }
        }
        #endregion

        #region  -- read and calculate  --

        //connect button
        private void button1_Click(object sender, EventArgs e)
        {
            if (comm.IsOpen != true)
            {
                if (comboBox1.Text == "") return;
                port = comboBox1.Text;
                comm = new SerialPort(port, 115200, Parity.None, 8, StopBits.One);
                comm.DataReceived += new SerialDataReceivedEventHandler(comm_DataReceived);
                try
                {
                    comm.Open();
                }
                catch
                {
                    MessageBox.Show("cannot find port","error");
                }
                if (comm.IsOpen)
                {
                    //comm.DiscardInBuffer();
                    read.Start();
                    calcirclethread.Start();
                    //timer1.Start();
                    timer2.Start();
                    comm.Write(STARTSCAN, 0, (int)STARTSCAN.LongLength);
                    comm.Write(STARTROTATE, 0, (int)STARTROTATE.LongLength);
                    sendalready = true;
                    button1.Text = "已連接";
                }
                else
                    MessageBox.Show("請連接正确的設備串口", "錯誤");
            }
        }

        //use the system timer to receivedata
        private void timer1_Tick(object sender, EventArgs e) {
            commread();
        }
        
        //byte that receive to hex
        public static string byteToHexStr(byte[] bytes)
        {
            string returnStr = "";
            if (bytes != null)
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    returnStr += bytes[i].ToString("X2");
                }
            }
            return returnStr;
        }

        //byte that receive to hex
        public static string onebyteToHexStr(byte bytes)
        {
            string returnStr = "";
            returnStr += bytes.ToString("X2");
            return returnStr;
        }

        //reverse the string
        public static string Reverse(string change, char[] arr = null)
        {
            arr = change.ToCharArray();
            Array.Reverse(arr);
            change = new string(arr);
            return change;
        }

        //use the q format and calculate the value
        public static double qchange(string input, int qnumber,object type)
        {
            //int start = 15 - input.Length;
            double value = 0;
            double lastvalue = 0;
            for (int fre = 0;fre < input.Length; fre++)
            {
                if (input.Substring(fre, 1) == "1")
                    value = Math.Pow(2, fre-qnumber) + lastvalue;
                lastvalue = value;
            }
            int i = 0;
            double d = 0;
            if (type.GetType() == d.GetType())
                return value;
            else if (type.GetType() == i.GetType())
                return (int)value;
            else
                return (int)value;
        }

        //add zero in the front
        public static string[] addzero(string[] input,int length1,int length2)
        {
            int Length1 = input[0].Length;
            int Length2 = input[1].Length;
            if (input[0].Length != length1)
                for (int fre2 = 0; fre2 < length1 - Length1; fre2++)
                    input[0] = "0" + input[0];

            if (input[1].Length != length2)
                for (int fre2 = 0; fre2 < length2 - Length2; fre2++)
                    input[1] = "0" + input[1];
            return input;
        }

        //check the timeout
        public bool GetcommResponseWTimeout(out byte[] outBuf, int expectedLength, int timeout)
        {
           // Console.WriteLine("::GetcommResponseWTimeout()");
            int idx = 0;
            outBuf = new byte[expectedLength];
            DateTime toAt = DateTime.Now + new TimeSpan(0, 0, 0, 0, timeout);
            bool timedOut = false;
            while (!timedOut)
            {
                if (comm.BytesToRead > 0)
                {
                    outBuf[idx++] = (byte)comm.ReadByte();
                    if (idx >= expectedLength)
                    {
                        //Console.WriteLine("  return true");
                        return true;
                    }
                }
                System.Threading.Thread.Sleep(5);
                if (DateTime.Now > toAt)
                    timedOut = true;
            }
            //Console.WriteLine("  return false");
            return false;
        }

        //request to the comm
        public void commRequest(Command cmd, byte[] payload = null)
        {
            byte chksum = 0x00;
            List<byte> buf = new List<byte>();
            buf.Add(0xA5); //start
            buf.Add((byte)cmd);
            if (payload != null && payload.Length > 0)
            {
                buf.Add((byte)payload.Length);
                buf.AddRange(payload);
                buf.ForEach(x => chksum ^= x);
                buf.Add(chksum);
            }
            byte[] b = buf.ToArray<byte>();
            comm.Write(b, 0, b.Length);
        }

        //receive data
        public void ReceiveData(string mode)
        {
            //startalready = 1;//warning
            
            //input the textbox's text
            input = mode;

            startshow = true;

            if (input.Length < 168)
                return;//break or wait the input completely

            //create a number of lastfre
            int lastfre = 0;

            //the number of cabin
            int noc = 0;

            //loop and check everywhere of the string
            for (int fre = lastfre; fre + 7 < input.Length; fre++)
            {
                double[] actangle = new double[3];
                string[] actdeg = new string[2];
                //if detect the data pachet is start transfering
                if (input.Substring(fre, 1) == "A" && input.Substring(fre + 2, 1) == "5")
                {
                    //input the two angle
                    string[] readangle = {Convert.ToString(Convert.ToInt64(input.Substring(fre + 4, 2), 16), 2),
                        Convert.ToString(Convert.ToInt64(input.Substring(fre + 6, 2), 16), 2) };

                    //if number's forward don't have "0",add it
                    readangle = addzero(readangle, 8, 8);

                    //startangle mean when start
                    startangle = Convert.ToInt32(readangle[1].Substring(0, 1));

                    StartOfNewScan = startangle == 1;

                    startalready = startangle | startalready;

                    //angle2 of the first one is startangle,so remove it
                    readangle[1] = readangle[1].Remove(0, 1);

                    //calculate binary code is right to left,so reverse them and will be convient for computer
                    readangle[1] = Reverse(readangle[1]);
                    readangle[0] = Reverse(readangle[0]);

                    if (StartOfNewScan)
                    {
                        watch.Stop();
                        deltatime = watch.ElapsedMilliseconds;
                        watch.Reset();
                        watch.Start();
                        StartOfNewScan = false;
                    }

                    //Binary code to decimal code
                    angle = readangle[0] + readangle[1];

                    //use the q format of 6
                    angle = qchange(readangle[0] + readangle[1], 6, typed).ToString();

                    //richTextBox1.AppendText("\n");
                    //richTextBox1.AppendText("S"+"\n");
                    //richTextBox1.AppendText(angle.ToString());
                    //double a = Convert.ToDouble(Convert.ToInt64(angle, 2)) / 64;
                    //angle = a.ToString();

                    if (startalready == 1)
                    {
                        int degfre = 0;
                        for (int fre2 = 0; fre2 < 16; fre2++)
                        {
                            if (noc == 160 || fre + 16 + noc > 166)
                                return;
                            //input the two distance and two deg
                            string[] distance1 = {Convert.ToString(Convert.ToInt64(input.Substring(fre + 8 + noc, 2), 16), 2),
                            Convert.ToString(Convert.ToInt64(input.Substring(fre + 10 + noc, 2), 16), 2) };
                            string[] distance2 = {Convert.ToString(Convert.ToInt64(input.Substring(fre + 12 + noc, 2), 16), 2),
                            Convert.ToString(Convert.ToInt64(input.Substring(fre + 14 + noc, 2), 16), 2) };
                            string deg = Convert.ToString(Convert.ToInt64(input.Substring(fre + 16 + noc, 2), 16), 2);

                            if (deg.Length != 8) {
                                int deglength = deg.Length;
                                for (int fre4 = 0; fre4 < 8 - deglength; fre4++)
                                    deg = "0" + deg;
                            }

                            //if number's forward don't have "0",add it
                            distance1 = addzero(distance1, 8, 8);
                            distance2 = addzero(distance2, 8, 8);

                            string[] deg1 = new string[2] { "0", "0" };
                            string[] deg2 = new string[2] { "0", "0" };
                            deg1[0] = distance1[0].Substring(distance1[0].Length - 2, 2);
                            deg1[1] = deg.Substring(4, 4);
                            deg2[0] = distance2[0].Substring(distance2[0].Length - 2, 2);
                            deg2[1] = deg.Substring(0, 4);

                            deg1 = addzero(deg1, 2, 4);
                            deg2 = addzero(deg2, 2, 4);

                            //remove some value
                            //distance1[0].Remove(distance1[0].Length - 2, 2);
                            //distance2[0].Remove(distance2[0].Length - 2, 2);

                            //substring
                            distance1[0] = distance1[0].Substring(0, distance1[0].Length - 2);
                            distance2[0] = distance2[0].Substring(0, distance2[0].Length - 2);

                            //calculate binary code is right to left,so reverse them and will be convient for computer
                            deg1[0] = Reverse(deg1[0]);
                            deg1[1] = Reverse(deg1[1]);
                            deg2[0] = Reverse(deg2[0]);
                            deg2[1] = Reverse(deg2[1]);

                            actdeg[0] = (deg1[1] + deg1[0]).Substring(1);
                            actdeg[1] = (deg2[1] + deg2[0]).Substring(1);
                            actdeg = addzero(actdeg, 6, 6);

                            actdeg[0] = (deg1[1] + deg1[0]).Remove((deg1[1] + deg1[0]).Length - 1, 1);
                            actdeg[1] = (deg2[1] + deg2[0]).Remove((deg2[1] + deg2[0]).Length - 1, 1);
                            //符号位去掉

                            int[] signbit = new int[2];
                            if ((deg1[1] + deg1[0]).Substring((deg1[1] + deg1[0]).Length - 1, 1) == "1")
                                signbit[0] = -1;
                            else
                                signbit[0] = 1;

                            if ((deg2[1] + deg2[0]).Substring((deg2[1] + deg2[0]).Length - 1, 1) == "1")
                                signbit[1] = -1;
                            else
                                signbit[1] = 1;

                            //second round
                            if (angle != "0" && angle != "" && lastangle != "" && loop == 1 && angle != lastangle)
                            {
                                //judgment the motor forward or reverse
                                if (Convert.ToDouble(lastangle) > Convert.ToDouble(angle))
                                    actangle[0] = 360 + Convert.ToDouble(angle) - Convert.ToDouble(lastangle);
                                else
                                    actangle[0] = Convert.ToDouble(angle) - Convert.ToDouble(lastangle);

                                //connect the words and convert to 
                                float[] distance = {Convert.ToInt64(distance1[1] + distance1[0], 2),
                                    Convert.ToInt64(distance2[1] + distance2[0], 2)};

                                //calculate the actual angle
                                actangle[1] = Convert.ToDouble(lastangle) + ((actangle[0] * (degfre+1)) / 32) - qchange(lastdeg[0,fre2], 3, typed) * signbit[0];
                                actangle[2] = Convert.ToDouble(lastangle) + ((actangle[0] * (degfre+2)) / 32) - qchange(lastdeg[1,fre2], 3, typed) * signbit[1];

                                //richTextBox1.AppendText("\n");
                                //richTextBox1.AppendText(actangle[1].ToString() + "\n");
                                //richTextBox1.AppendText(distance[0].ToString() + "\n");
                                //richTextBox1.AppendText("\n");
                                //richTextBox1.AppendText(actangle[2].ToString() + "\n");
                                //richTextBox1.AppendText(distance[1].ToString() + "\n");

                                //input all the thing
                                ScanData[degfre,group] = new ScanPoint
                                {
                                    Distance = distance[0],
                                    Angle = actangle[1],
                                    Quantity = group
                                };
                                ScanData[degfre+1,group] = new ScanPoint
                                {
                                    Distance = distance[1],
                                    Angle = actangle[2],
                                    Quantity = group
                                };
                                if (group == showfre)
                                    group = 0;
                                //the cabin
                            }
                            lastdeg[0,fre2] = actdeg[0];
                            lastdeg[1,fre2] = actdeg[1];
                            noc += 10;
                            degfre += 2;
                        }
                        //show the number
                        //record the angle
                        //let computer can know this is the first round
                        loop = 1;
                        //one data packet has 84bytes, so have 168 numbers
                        lastfre += 168;
                        StartOfNewScan = true;
                    }
                    if (angle != "0")
                        lastangle = angle;
                }
            }
            group += 1;
        }

        #region -- test --

        private List<byte> RevBuffer = new List<byte>();

        private void commread()
        {
            lock (RevBuffer)
            {
                while (true)
                {
                    buffer = new byte[length];
                    while (ProcessCommand()) ;      //處理收到的所有消息，知道再沒有任務
                    if((checklen = comm.Read(buffer, 0, buffer.Length)) <= length)
                            RevBuffer.AddRange(buffer.Take(checklen));
                }
            }
        }

        private void comm_DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            commread();
        }

        private bool ProcessCommand()
        {
            lock (RevBuffer)
            {
                checklen = 0;
                var len = RevBuffer.Count;
                if (len < length)      
                    return false;

                var command = new byte[length+errornumber-1];
                Array.Copy(RevBuffer.ToArray(), command, length+errornumber-1);
                if (checkalready || length == 7)
                    RevBuffer.RemoveRange(0, length + errornumber - 1);      
                if (length == 84 && checkalready == false)
                {
                    if (startscan)
                    {
                        if (RevBuffer.Count >= 1 + errornumber)
                        {
                            if (onebyteToHexStr(RevBuffer[0 + errornumber - 1]).Substring(0, 1) == "A"
                                && onebyteToHexStr(RevBuffer[1 + errornumber - 1]).Substring(0, 1) == "5")
                                checkalready = true;
                        }
                    }
                    errornumber++;
                }
                if (checkalready)
                {
                    //richTextBox1.AppendText(byteToHexStr(command));
                    ReceiveData(byteToHexStr(command));
                    errornumber = 1;
                }
                if (byteToHexStr(command) == "A55A5400004082" && startscan != true) {
                    startscan = true;
                    length = 84; 
                }
                return true;    //通知调用程序，收到了一个任务
            }
        }

        #endregion

        private void button2_Click(object sender, EventArgs e)
        {
            //byte[] command = System.Text.Encoding.Default.GetBytes(textBox1.Text);
            //LidarScanData node = command.ByteArrayToStructure<LidarScanData>(0);
            //float distance = node.Distance / 4f;
            //int angle = (int)((node.Angle >> 1) / 64.0);
            //int quality = node.Quality >> 2;
            //if (distance > 0 && angle < 360)
            //    ScanData[0, 0] = new ScanPoint { Angle = angle, Quantity = quality, Distance = distance };
            ReceiveData(textBox1.Text);
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            //if (startshow)
            //{
            //    richTextBox1.AppendText(input);
            //    startshow = false;
            //}
            pictureBox1.Invalidate();
        }

        private void readvalue()
        {
            System.Timers.Timer servotimer = new System.Timers.Timer();
            servotimer.Interval = 1;
            servotimer.Elapsed += new System.Timers.ElapsedEventHandler(timer1_Tick);
            servotimer.Enabled = true;
        }

        private void calcircle(object sender, EventArgs e)
        {
            //nicecircle = 
        }

        private void circlethread()
        {
            System.Timers.Timer servotimer = new System.Timers.Timer();
            servotimer.Interval = 1;
            servotimer.Elapsed += new System.Timers.ElapsedEventHandler(calcircle);
            servotimer.Enabled = true;
        }

        #endregion

        #region -- draw the picture --

        void drawline(Graphics g,Pen p,int input,double distance)
        {
            float angle = (float)Math.PI * input / 180;
            g.DrawLine(p, (float)(m_ptCanvas.X - Math.Sin(angle) * distance),
                       (float)(m_ptCanvas.Y - Math.Cos(angle) * distance), m_ptCanvas.X, m_ptCanvas.Y);
        }

        void drawpoint(Graphics g,Pen p,int input,double distance)
        {
            float angle = (float)Math.PI * input / 180;
            g.DrawLine(p, (float)(m_ptCanvas.X - Math.Sin(angle) * distance),
                        (float)(m_ptCanvas.Y - Math.Cos(angle) * distance),
                        (float)(m_ptCanvas.X - Math.Sin(angle) * distance),
                        (float)(m_ptCanvas.Y - Math.Cos(angle) * distance));
        }

        private Circle FittingCircle(int[,] X,int[,] Y)
        {
            Circle pCircle = new Circle();
            double X1 = 0;
            double Y1 = 0;
            double X2 = 0;
            double Y2 = 0;
            double X3 = 0;
            double Y3 = 0;
            double X1Y1 = 0;
            double X1Y2 = 0;
            double X2Y1 = 0;
            for (int i = 0; i < 32; i++)
            {
                for (int i2 = 0; i2 < 31; i2++)
                {
                    X1 = X1 + X[i, i2];
                    Y1 = Y1 + Y[i,i2];
                    X2 = X2 + X[i,i2] * X[i,i2];
                    Y2 = Y2 + Y[i,i2] * Y[i,i2];
                    X3 = X3 + X[i,i2] * X[i,i2] * X[i,i2];
                    Y3 = Y3 + Y[i,i2] * Y[i,i2] * Y[i,i2];
                    X1Y1 = X1Y1 + X[i,i2] * Y[i,i2];
                    X1Y2 = X1Y2 + X[i,i2] * Y[i,i2] * Y[i,i2];
                    X2Y1 = X2Y1 + X[i,i2] * X[i,i2] * Y[i,i2];
                }
            }
            double C, D, E, G, H, N;
            double a, b, c;
            N = X.Length;
            C = N * X2 - X1 * X1;
            D = N * X1Y1 - X1 * Y1;
            E = N * X3 + N * X1Y2 - (X2 + Y2) * X1;
            G = N * Y2 - Y1 * Y1;
            H = N * X2Y1 + N * Y3 - (X2 + Y2) * Y1;
            a = (H * D - E * G) / (C * G - D * D);
            b = (H * C - E * D) / (D * D - G * C);
            c = -(a * X1 + b * Y1 + X2 + Y2) / N;
            pCircle.X = a / (-2);
            pCircle.Y = b / (-2);
            pCircle.R = Math.Sqrt(a * a + b * b - 4 * c) / 2;
            return pCircle;
        }

        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TranslateTransform(m_ptCanvas.X, m_ptCanvas.Y);       //设置坐标偏移
            g.DrawImage(m_bmp, m_ptBmp);                            //绘制图像

            g.ResetTransform();                                     //重置坐标系
            Pen p = new Pen(Color.LightCyan, 1.2F);
            Rectangle r1 = new Rectangle(new Point(0,0)
                                        , new Size(pictureBox1.Width,pictureBox1.Height));
            g.DrawEllipse(p,r1);        //draw a circle
            g.DrawLine(p, 0, m_ptCanvas.Y, pictureBox1.Width, m_ptCanvas.Y);    //draw line
            g.DrawLine(p, m_ptCanvas.X, 0, m_ptCanvas.X, pictureBox1.Height);   //draw line
            for (int fre = 0; fre < 360; fre += 30)
                drawline(g,p,360-fre,pictureBox1.Width / 2);    //every 30deg draw a line

            double pro = 0;     //proportion
            if(double.TryParse(label14.Text, out pro))
                pro = (pictureBox1.Width/2)/pro; //try to calculate the proportion
            //check every value in the ScanData
            for (int fre = 0; fre < showfre; fre++)    
            {
                for (int gp = 0; gp < 32; gp++)
                {
                    if (m_ptMouseMove.X != 0 && m_ptMouseMove.Y != 0) //avoid that dividing 0
                    {
                        int mouseangle = (int)((Math.Atan((float)(m_ptMouseMove.Y - m_ptCanvas.Y)
                            / (float)-(m_ptMouseMove.X - m_ptCanvas.X)) * 180) / Math.PI);
                        //calculate the mouse point to the origin point's angle

                        //calculate the angle that actually we need
                        if (m_ptMouseMove.X - m_ptCanvas.X <= 0)
                            mouseangle = 180 + 180 - mouseangle;
                        mouseangle = Math.Abs(mouseangle - 90);
                        showangle = mouseangle;
                        label2.Text = showangle.ToString();

                        //if the proportion is not good then will zorm it
                        //while (Convert.ToInt32(label14.Text) <= ScanData[gp,fre].Distance)
                            //label14.Text = (Convert.ToInt32(label14.Text) + 3000).ToString();

                        //if the mouseangle equal the distance's angle then draw line
                        //or show the oldline
                        if (mouseangle == ScanData[gp,fre].Angle)
                        {
                            drawline(g, new Pen(Color.Red), 360 - mouseangle, ScanData[gp,fre].Distance * pro);
                            label3.Text = (mouseangle).ToString();
                            oldangle = 360 - mouseangle;
                            olddistance = ScanData[gp,fre].Distance;
                        }
                        else
                            drawline(g, new Pen(Color.Red), oldangle, olddistance * pro);
                    }
                    if (ScanData[gp, fre].Angle != 0 && ScanData[gp, fre].Distance != 0)
                    {
                        double pieangle = (Math.PI * (330 - ScanData[gp, fre].Angle) / 180);
                        Point detectpoint = new Point((int)(m_ptCanvas.X - Math.Sin(pieangle) * ScanData[gp, fre].Distance * pro),
                            (int)(m_ptCanvas.Y - Math.Cos(pieangle) * ScanData[gp, fre].Distance * pro));
                        recordx[gp,fre] = detectpoint.X;
                        recordy[gp,fre] = detectpoint.Y;
                        int r = 2;
                        Rectangle rectpie = new Rectangle((int)(m_ptCanvas.X - Math.Sin(pieangle) * ScanData[gp, fre].Distance * pro) -r,
                            (int)(m_ptCanvas.Y - Math.Cos(pieangle) * ScanData[gp, fre].Distance * pro) - r,r,r);
                        //g.FillPie(Brushes.Green, rectpie, 0, 360);
                        g.DrawEllipse(new Pen(Color.Green), rectpie);
                        //drawpoint(g, new Pen(Color.Red), 360 - ScanData[fre].Angle, ScanData[fre].Distance * pro);
                        //g.DrawEllipse(new Pen(Color.Red), new RectangleF((float)(FittingCircle(recordx,recordy).X), (float)(FittingCircle(recordx, recordy).Y)
                            //, (float)(FittingCircle(recordx, recordy).R), (float)(FittingCircle(recordx, recordy).R)));
                    }
                }
            }
            p.Dispose();
        }

        private void pictureBox1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (m_nScale < -58 && e.Delta <= 0) return;        //缩小下线
            //if (m_nScale > 66 && e.Delta >= 0) return;        //放大上线
            //if (m_nScale == 66) label14.Text = "30000";
            else if (m_nScale == -58) label14.Text = "1000";
            else
            {
                if (e.Delta > 0) label14.Text = (Convert.ToInt32(label14.Text) + 120).ToString();
                else if (e.Delta < 0) label14.Text = (Convert.ToInt32(label14.Text) - 120).ToString();
            }
            m_nScale += e.Delta > 0 ? 1F : -1F;
            pictureBox1.Invalidate();
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            m_ptMouseMove = e.Location;
            //pictureBox1.Invalidate();

        }

        private void pictureBox1_MouseClick(object sender, MouseEventArgs e)
        {
            MessageBox.Show(showangle.ToString());
        }

        #endregion

        private void button3_Click(object sender, EventArgs e)
        {
            timer2.Stop();
            if (comm.IsOpen)
            {
                comm.Write(STOPSCAN, 0, (int)STOPSCAN.LongLength);
                comm.Write(STOPROTATE, 0, (int)STOPROTATE.LongLength);
            }
        }

        private void timer3_Tick(object sender, EventArgs e)
        {
            if(deltatime != 0)
                label5.Text = (60 /(deltatime/1000)).ToString();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            DRAWCIRCLE = true;
        }
    }
    public enum Command : byte
    {
        Stop = 0x25,
        Reset = 0x40,
        Scan = 0x20,
        ForceScan = 0x21,
        GetInfo = 0x50,
        GetHealth = 0x52,
        StartMotor = 0xF0
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ScanPoint
    {
        public double Angle;
        public float Distance;
        public int Quantity;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Circle
    {
        public double X;//圆心X
        public double Y;//圆心Y
        public double R;//半径R
    }

    /// <summary>
    /// 定义的拟合点
    /// </summary>
    public struct PixelPoint
    {
        public double X;
        public double Y;
    }


    //http://blog.csdn.net/lijiayu2015/article/details/52541730
    //CircleData findCircle1(Point pt1, Point pt2, Point pt3)
    //{
    //    //定义两个点，分别表示两个中点  
    //    Point midpt1, midpt2 = new Point(0, 0);
    //    midpt1 = new Point((pt2.X + pt1.X) / 2, (pt2.Y + pt1.Y) / 2);
    //    midpt2 = new Point((pt3.X + pt1.X) / 2, (pt3.Y + pt1.Y) / 2);

    //    //求出分别与直线pt1pt2，pt1pt3垂直的直线的斜率  
    //    float k1 = -(pt2.X - pt1.X) / (pt2.Y - pt1.Y);
    //    float k2 = -(pt3.X - pt1.X) / (pt3.Y - pt1.Y);
    //    CircleData CD;
    //    CD.center = new PointF((midpt2.Y - midpt1.Y - k2 * midpt2.X + k1 * midpt1.X) / (k1 - k2)
    //        , midpt1.Y + k1 * (midpt2.Y - midpt1.Y - k2 * midpt2.X + k2 * midpt1.X) / (k1 - k2));
    //    //用圆心和其中一个点求距离得到半径：
    //    CD.radius = Math.Sqrt((CD.center.X - pt1.X) * (CD.center.X - pt1.X) + (CD.center.Y - pt1.Y) * (CD.center.Y - pt1.Y));
    //    return CD;
    //}

    //CircleData findCircle2(Point pt1, Point pt2, Point pt3)
    //{
    //    double A1, A2, B1, B2, C1, C2, temp;
    //    A1 = pt1.X - pt2.X;
    //    B1 = pt1.Y - pt2.Y;
    //    C1 = (Math.Pow(pt1.X, 2) - Math.Pow(pt2.X, 2) + Math.Pow(pt1.Y, 2) - Math.Pow(pt2.Y, 2)) / 2;
    //    A2 = pt3.X - pt2.X;
    //    B2 = pt3.Y - pt2.Y;
    //    C2 = (Math.Pow(pt3.X, 2) - Math.Pow(pt2.X, 2) + Math.Pow(pt3.Y, 2) - Math.Pow(pt2.Y, 2)) / 2;
    //    //为了方便编写程序，令temp = A1*B2 - A2*B1  
    //    temp = A1 * B2 - A2 * B1;
    //    //定义一个圆的数据的结构体对象CD  
    //    CircleData CD;
    //    //判断三点是否共线  
    //    if (temp == 0)//共线则将第一个点pt1作为圆心  
    //        CD.center = pt1;
    //    else
    //        CD.center = new PointF((float)((C1 * B2 - C2 * B1) / temp)
    //            , (float)((A1 * C2 - A2 * C1) / temp));
    //    CD.radius = Math.Sqrt((CD.center.X - pt1.X) * (CD.center.X - pt1.X) + (CD.center.Y - pt1.Y) * (CD.center.Y - pt1.Y));
    //    return CD;
    //}
    ////TESTING for RPLIDAR judgment object
    //CircleData mcircle1 = findCircle1(new Point(oldx[0], oldy[0]),
    //    new Point(oldx[1], oldy[1]), new Point(oldx[2], oldy[2]));
    //Rectangle threepointcircle1 = new Rectangle((int)(mcircle1.center.X - mcircle1.radius)
    //    , (int)(mcircle1.center.Y - mcircle1.radius), (int)mcircle1.radius * 2, (int)mcircle1.radius * 2);
    //g.DrawEllipse(new Pen(Color.Green), threepointcircle1);

    //CircleData mcircle2 = findCircle1(new Point(oldx[0], oldy[0]),
    //     new Point(oldx[1], oldy[1]), new Point(oldx[2], oldy[2]));
    //Rectangle threepointcircle2 = new Rectangle((int)(mcircle2.center.X - mcircle2.radius)
    //    , (int)(mcircle1.center.Y - mcircle1.radius), (int)mcircle2.radius * 2, (int)mcircle2.radius * 2);
    //g.DrawEllipse(new Pen(Color.Green), threepointcircle2);
}
