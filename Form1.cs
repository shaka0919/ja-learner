using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MeCab;
using System.Text;
using System.Reflection.Metadata;

namespace ja_learner
{
    public partial class Form1 : Form
    {

        // 导入 Windows API 函数
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rectangle rect);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point lpPoint);
        // 根据鼠标位置获取目标窗口句柄hwnd
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

        // 根据hwnd获取窗口标题
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder title, int size);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto)]
        static extern IntPtr GetParent(IntPtr hWnd);

        // 定义 RECT 结构体
        [StructLayout(LayoutKind.Sequential)]
        public struct Rectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // 定义 POINT 结构体
        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }


        IDisposable _server = null;

        private MeCabParam parameter;
        private MeCabTagger tagger;

        DictForm dictForm;


        private string sentence = "文の敬体(ですます調)、常体(である調)を解析するJavaScriptライブラリ";

        public Form1()
        {
            InitializeComponent();
            // DisableCors();
        }
        private async Task InitializeWebView()
        {
            await webView.EnsureCoreWebView2Async(null);
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Light;
            // 处理打开新窗口（用默认浏览器打开）
            webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            // 接收js消息（查词典）
            webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        }
        private async void Form1_Load(object sender, EventArgs e)
        {
            // 初始化 webview
            await InitializeWebView();

#if DEBUG
            webView.Source = new Uri("http://localhost:5173/"); // dev
#else
            // 初始化 HTTP 服务器
            HttpServer.StartServer();
            webView.Source = new Uri("http://localhost:8080/"); // build
#endif

            // 初始化 mecab dotnet
            parameter = new MeCabParam();
            // 读取用户词典
            string[] userdic = Directory.GetFiles("dic/userdic");
            for (int i = 0; i < userdic.Length; i++)
            {
                userdic[i] = "userdic/" + Path.GetFileName(userdic[i]);
            }
            parameter.UserDic = userdic;
            tagger = MeCabTagger.Create(parameter);

            UpdateMecabResult(RunMecab());
            dictForm = new DictForm(this);
            dictForm.Show();
            dictForm.Hide();
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true; // 防止WebView2打开新窗口

            // 使用默认浏览器打开链接
            Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
        }
        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message = e.TryGetWebMessageAsString();
            dictForm.SearchText(message);
        }

        private string RunMecab()
        {
            string result = "[";
            foreach (var node in tagger.ParseToNodes(sentence))
            {
                if (node.CharType > 0)
                {
                    var features = node.Feature.Split(',');
                    var displayFeatures = string.Join(", ", features);
                    // features[0] 是词性，[6] 是原型，[7] 是发音（如果有的话）
                    string pos = features[0];
                    string basic = features[6];
                    string reading = features.Length > 7 ? features[7] : "";
                    result += $"{{surface:'{node.Surface}',pos:'{pos}',basic:'{basic}',reading:'{reading}'}},";
                }
            }
            result += "]";
            return result;
        }

        private async void UpdateMecabResult(string json)
        {
            string result = await webView.ExecuteScriptAsync($"updateData({json})");

        }

        private void Form1_SizeChanged(object sender, EventArgs e)
        {
            if (timerWindowAlign.Enabled)
            {
                heightAfter = this.Height;
            }
        }


        private System.Drawing.Point locationBefore; // 记录普通模式下窗口的位置
        private Size sizeBefore; // 记录普通模式下窗口的大小
        private int heightAfter = 200; // 附着模式时，窗体通常会比较矮

        private void checkBoxAlignWindow_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxAlignWindow.Checked)
            {
                // 记录普通状态的窗口位置，切换到吸附状态下的窗口位置
                sizeBefore = this.Size;
                locationBefore = this.Location;
                this.Height = heightAfter;
                timerWindowAlign.Enabled = true;
            }
            else
            {
                timerWindowAlign.Enabled = false;
                heightAfter = this.Height;
                this.Size = sizeBefore;
                this.Location = locationBefore;
            }
        }

        private void timerWindowAlign_Tick(object sender, EventArgs e)
        {
            IntPtr hwnd = IntPtr.Parse(textBoxHwnd.Text);
            try
            {
                // 调用 GetWindowRect 函数获取窗口位置和大小
                Rectangle rect;
                if (GetWindowRect(hwnd, out rect))
                {
                    this.Top = rect.Bottom;
                    this.Left = rect.Left;
                    this.Width = rect.Right - rect.Left; // 对齐宽度

                    dictForm.Top = rect.Top;
                    dictForm.Left = rect.Right;
                    dictForm.Height = this.Bottom - rect.Top;
                }
            }
            catch (Exception ex)
            {
                WindowAlign = false;
            }
        }

        private bool windowAlign = false;

        public bool WindowAlign
        {
            get
            {
                return windowAlign;
            }
            set
            {
                windowAlign = value;
                checkBoxAlignWindow.Checked = windowAlign;
                timerWindowAlign.Enabled = windowAlign;
            }
        }

        private void checkBoxClipboardMode_CheckedChanged(object sender, EventArgs e)
        {
            ClipBoardMode = checkBoxClipboardMode.Checked;
        }
        private void timerGetClipboard_Tick(object sender, EventArgs e)
        {
            string newSentence = Clipboard.GetText(TextDataFormat.UnicodeText).Trim();
            if (newSentence != sentence)
            {
                sentence = newSentence;
                UpdateMecabResult(RunMecab());
            }
        }
        public bool ClipBoardMode
        {
            get
            {
                return checkBoxClipboardMode.Checked;
            }
            set
            {
                checkBoxClipboardMode.Checked = value;
                timerGetClipboard.Enabled = value;
                btnInputText.Enabled = !value;
            }
        }

        private void btnInputText_Click(object sender, EventArgs e)
        {
            sentence = Microsoft.VisualBasic.Interaction.InputBox("Prompt", "Title", "", 0, 0);
            string json = RunMecab();
            UpdateMecabResult(json);
        }

        private void timerSelectWindow_Tick(object sender, EventArgs e)
        {
            Point cursorPos = new Point();
            cursorPos.X = Cursor.Position.X;
            cursorPos.Y = Cursor.Position.Y;
            // 当前鼠标位置所在窗口的最高父窗口的hwnd
            IntPtr hwnd = WindowFromPoint(cursorPos);
            IntPtr parentHwnd;
            while (true)
            {
                parentHwnd = GetParent(hwnd);
                if (parentHwnd == IntPtr.Zero) break;
                hwnd = parentHwnd;
            }
            textBoxHwnd.Text = hwnd.ToString();

            // 判断鼠标是否按下
            bool mouseDown = (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left;

            if (mouseDown)
            {
                timerSelectWindow.Enabled = false;
            }
        }

        private void btnSelectWindow_Click(object sender, EventArgs e)
        {
            WindowAlign = false;
            timerSelectWindow.Enabled = true;
        }

        private void checkBoxDark_CheckedChanged(object sender, EventArgs e)
        {
            webView.CoreWebView2.Profile.PreferredColorScheme = checkBoxDark.Checked ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        }

        private void buttonShowDictForm_Click(object sender, EventArgs e)
        {
            dictForm.Show();
            dictForm.WindowState = FormWindowState.Normal; // 从最小化状态到普通状态
            dictForm.BringToFront();
            dictForm.Activate();
        }

        public static String GetWindowTitle(IntPtr handle)
        {
            int length = GetWindowTextLength(handle);
            StringBuilder text = new StringBuilder(length + 1);
            GetWindowText(handle, text, text.Capacity);
            return text.ToString();
        }
        private void textBoxHwnd_TextChanged(object sender, EventArgs e)
        {
            try
            {
                IntPtr hwnd = IntPtr.Parse(textBoxHwnd.Text);
                string windowTitle = GetWindowTitle(hwnd);
                checkBoxAlignWindow.Text = "与【" + windowTitle + "】对齐";
                // 判断窗口句柄是不是自己的
                if (hwnd == this.Handle || hwnd == dictForm.Handle)
                {
                    checkBoxAlignWindow.Enabled = false;
                    return;
                }
                checkBoxAlignWindow.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

    }
}