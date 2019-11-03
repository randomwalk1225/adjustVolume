using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
using System.Windows.Threading;

namespace WpfApp1_ffmpegvolume
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        private Process proc = null;
        private Boolean IsUserStop = false;     //사용자가 작업을 취소했는지 여부
        private long vodSize;             //동영상 파일의 크기

        public MainWindow()
        {
            InitializeComponent();
        }

        private void ButtonSearch_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog diag1 = new OpenFileDialog();
            diag1.Filter = "Video files (*.mp4,*.avi)| *.mp4;*.avi |All files (*.*)| *.*";
            diag1.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            
            if (diag1.ShowDialog() == true) {
                string filefullpath = diag1.FileName;
                string filepath = System.IO.Path.GetDirectoryName(filefullpath);
                string filename = System.IO.Path.GetFileName(filefullpath);
                string filenwithoutext = System.IO.Path.GetFileNameWithoutExtension(filefullpath);
                string fileExtension = System.IO.Path.GetExtension(filefullpath);   //파일 확장자
                //고칠부분: 파일이름에 확장자가 따라옴
                //필터에 *.mp4가 자동으로 잡히지 않음

                textBox1.Text = filefullpath;
                textBox2.Text = filepath + "\\" + filenwithoutext + "_volume_UP" + fileExtension;

            }
        }

        //실행버튼 누르면 실행되는 부분
        private void ButtonRun_Click(object sender, RoutedEventArgs e)
        {
            if (proc != null)
            {
                if (proc.HasExited == false)
                {
                    MessageBox.Show("프로세스가 이미 사용중 입니다.");
                    return;
                }
            } else if (File.Exists(textBox2.Text))
            {
                if (MessageBox.Show("동일한 파일명이 존재합니다.\n해당 파일에 덮어씌우겠습니까?","",MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    return;
                } else
                {
                    //동일한 파일이 존재하면 삭제 시킨다.
                    //  배포용으로 제작시 삭제 후 볼륨제어 작업을 수행 중 오류로 인하여 작업이 중단되는 경우 삭제시킨 파일을 복구할 수 없게되므로,
                    //  [임시파일명으로 작업 -> 동일한 파일을 삭제 -> 작업된 파일의 파일명을 변경]과 같은 순서로 작업해야 한다.
                    //  (*** 실제는 삭제시킬 대상 파일을 사용자가 변경할 수 없도록 파일명을 변경/속성변경 등의 추가 작업이 수행된다.)
                    File.Delete(textBox2.Text);
                }
            }

            string item = combo1.Text;
            textBlock1.Text = "";

            proc = new Process();

            proc.StartInfo.FileName = "C:\\ffmpeg\\ffmpeg-latest-win64-static\\bin\\ffmpeg.exe";
            //ffmpeg - i inputfile - vcodec copy - af "volume=10dB" outputfile        
            proc.StartInfo.Arguments = "-i " + textBox1.Text + " -vcodec copy -af \"volume=" + item + "\" " + textBox2.Text; // Note the /c command (*)
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;

            proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            proc.StartInfo.CreateNoWindow = true;

            proc.EnableRaisingEvents = true;
            proc.Exited += new EventHandler(ProcessExited);     //Process가 종료될 때 이벤트

            IsUserStop = false;
            vodSize = new FileInfo(textBox1.Text).Length;
            pBar.Maximum = 100;
            pBar.Value = 0;
            buttonRun.IsEnabled = false;
            buttonCancel.IsEnabled = true;

            proc.Start();

            //쓰레드를 생성과 동시에 시작
            Task.Factory.StartNew(new Action(delegate ()
            {
                ProgressStatus();
            }));


            //아래 행의 코드는 ProgressStatus() 함수에서 처리하므로 주석처리한다.

            ////commandline 
            //textBlock1.Text = "C:\\ffmpeg\\ffmpeg-20190821-661a9b2-win64-static\\bin\\ffmpeg.exe "
            //    + "-i " + textBox1.Text + " -vcodec copy -af \"volume="+item+"\" " + textBox2.Text;

            ////* Read the output (or the error)
            //string output = proc.StandardOutput.ReadToEnd();
            //textBlock1.Text = textBlock1.Text +'\n' +"실행";
            //textBlock1.Text = textBlock1.Text + output;
            //string err = proc.StandardError.ReadToEnd();
            //textBlock1.Text = textBlock1.Text + '\n' + err;
            //proc.WaitForExit();

        }

        //취소버튼
        private void buttonCancel_Click(object sender, RoutedEventArgs e)
        {
            if (proc == null)
            {
                return;
            }

            proc.StandardInput.WriteLine("q");      //Process 창의 작업을 중지시킨다.

            buttonCancel.IsEnabled = false;
            IsUserStop = true;
        }

        //Process 창에 표시되는 문자열을 가져와서 작업한다.
        private void ProgressStatus()
        {
            StreamReader sr = proc.StandardError;
            string readStr;
            long prgSize;
            double fileSize = vodSize;
            double pVal = 0;

            try
            {
                while (proc != null)    //Process 창이 종료될때까지 반복한다.
                {
                    readStr = sr.ReadLine();            //Process 창에 표시되는 문자를 가져온다.

                    //이곳에서 "readStr" 변수의 문자열을 분석하여 ffmpeg.exe에서 오류가 발생한 경우에 대한 처리를 해야한다.
                    //  오류 처리를 위해서는 ffmpeg.exe에서 Process 창에 표시하는 오류 메세지의 유형을 알아야한다.

                    prgSize = GetPrgSize(readStr);      //Process 창에 표시된 문자열에서 처리된 바이트 수를 가져온다.

                    if (prgSize > 0)        //처리된 바이트수가 존재하는 경우
                    {
                        pVal = (prgSize / fileSize) * 100;   //처리된 바이트 수를 백분율로 구한다.
                    }

                    //Invoke를 이용하여 다른 쓰레드의 UI에 접근한다.
                    Application.Current.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate () {
                        //프로그래스바의 진행값을 변경한다.
                        pBar.Value = pVal;

                        //Process 창에 표시된 문자열을 화면에 표시한다.
                        //  파일 용량이 큰 경우 TextBox에 문자열을 표시하면서 속도가 많이 느려지게 된다.
                        //  반듯이 표시해야되는 경우 최근 몇개의 행만 표시하는것이 좋음.
                        textBlock1.Text += readStr + "\n";  
                    }));
                }
            } catch(Exception e) {
                MessageBox.Show(e.Message);
            }
        }

        //진행된 파일의 크기를 구해서 반환한다.
        private long GetPrgSize(String str)
        {
            if (str != null && str != "")
            {
                int pos1, pos2;
                string pSize;

                pos1 = str.IndexOf("size=");    //readStr 변수의 문자열내에서 "size=" 문자열의 위치 찾기
                pos2 = str.IndexOf("time=");

                //readStr 변수에 위의 문자열이 모두 존재하는 경우. pos2에서 찾은 위치는 반듯이 pos1 보다 커야한다.
                if (pos1 >= 0 && pos2 > pos1)
                {
                    //pos1의 위치에서 "size=" 문자열의 수(5개)를 더해야 "size=" 문자열 이후의 문자부터 가져오게 된다.
                    //  샘플 문자열: frame= 4631 fps=4625 q=-1.0 size=    6144kB time=00:01:18.95 bitrate= 637.5kbits/s speed=78.8x
                    pSize = str.Substring(pos1 + 5, pos2 - pos1 - 5).Trim();

                    if (pSize.Substring(pSize.Length -2).ToLower() == "kb")
                    {
                        pSize = pSize.Substring(0, pSize.Length - 2) + "000";
                    } else if (pSize.Substring(pSize.Length - 2).ToLower() == "mb")
                    {
                        pSize = pSize.Substring(0, pSize.Length - 2) + "000000";
                    }

                    return Convert.ToInt64(pSize);    //진행된 파일크기(위 샘플 문자열에서는 "6144kB" 문자열을 반환하게 된다.)
                }
            }

            return 0;  //진행된 크기를 찾지 못한경우 "0"을 리턴한다.
        }

        //Process가 종료될 때
        //  ffmpeg.exe 에서 오류가 발생하여 종료되는 경우에 대한 처리가 필요하다.
        private void ProcessExited(Object sender, EventArgs e)
        {
            if (proc != null)
            {
                proc.Dispose();
                proc = null;
            }

            Application.Current.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(delegate () {
                buttonRun.IsEnabled = true;
                buttonCancel.IsEnabled = false;

                if (IsUserStop == true)     //사용자가 작업을 취소한 경우
                {
                    pBar.Value = 0;
                    MessageBox.Show("취소 되었습니다.");
                } else
                {
                    pBar.Value = pBar.Maximum;
                    MessageBox.Show("변환이 완료되었습니다.");
                }
            }));
        }

    }
}
