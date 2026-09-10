using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NetPulse
{
    public sealed class App : Application
    {
        [STAThread]
        public static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            if (args.Length >= 2 && args[0] == "--smoke-test")
            {
                try
                {
                    Uri smokeEndpoint = new Uri(args.Length >= 3 ? args[2] : "https://speed.cloudflare.com");
                    string result = SpeedTestEngine.RunSmokeAsync(smokeEndpoint, CancellationToken.None).GetAwaiter().GetResult();
                    File.WriteAllText(args[1], result);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(args[1], "FAILED: " + ex.ToString());
                    Environment.ExitCode = 1;
                }
                return;
            }

            App app = new App();
            MainWindow window = new MainWindow();

            if (args.Length >= 2 && args[0] == "--screenshot")
            {
                window.Loaded += delegate
                {
                    window.ApplyDemoData();
                    window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                    {
                        window.RenderToPng(args[1]);
                        window.Close();
                    }));
                };
            }

            app.Run(window);
        }
    }

    public sealed class MainWindow : Window
    {
        private static readonly Color BackgroundColor = Color.FromRgb(10, 15, 28);
        private static readonly Color PanelColor = Color.FromRgb(20, 28, 46);
        private static readonly Color MutedColor = Color.FromRgb(143, 158, 183);
        private static readonly Color AccentColor = Color.FromRgb(54, 211, 153);
        private static readonly Color BlueColor = Color.FromRgb(78, 154, 255);

        private readonly TextBlock latencyValue;
        private readonly TextBlock jitterValue;
        private readonly TextBlock downloadValue;
        private readonly TextBlock uploadValue;
        private readonly TextBlock statusText;
        private readonly TextBlock liveRateText;
        private readonly TextBox endpointBox;
        private readonly Button startButton;
        private readonly Button cancelButton;
        private readonly ProgressBar progressBar;
        private readonly ListView historyList;
        private CancellationTokenSource cancellation;

        public MainWindow()
        {
            Title = "NetPulse - 网络测速";
            Width = 1080;
            Height = 760;
            MinWidth = 920;
            MinHeight = 660;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brush(BackgroundColor);
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
            Foreground = Brushes.White;

            Grid root = new Grid();
            root.Margin = new Thickness(34, 26, 34, 22);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(166) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(126) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });

            StackPanel brand = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock title = Text("NetPulse", 28, FontWeights.SemiBold, Brushes.White);
            TextBlock subtitle = Text("轻量、透明的 Windows 网络质量检测", 13, FontWeights.Normal, Brush(MutedColor));
            subtitle.Margin = new Thickness(1, 5, 0, 0);
            brand.Children.Add(title);
            brand.Children.Add(subtitle);
            header.Children.Add(brand);

            StackPanel endpointPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock endpointLabel = Text("测速服务地址", 12, FontWeights.Medium, Brush(MutedColor));
            endpointLabel.Margin = new Thickness(0, 0, 0, 6);
            endpointPanel.Children.Add(endpointLabel);
            endpointBox = new TextBox
            {
                Text = "https://speed.cloudflare.com",
                Height = 38,
                FontSize = 13,
                Padding = new Thickness(12, 8, 12, 8),
                Foreground = Brushes.White,
                Background = Brush(Color.FromRgb(28, 38, 59)),
                BorderBrush = Brush(Color.FromRgb(55, 70, 96)),
                BorderThickness = new Thickness(1),
                CaretBrush = Brushes.White
            };
            endpointPanel.Children.Add(endpointBox);
            Grid.SetColumn(endpointPanel, 1);
            header.Children.Add(endpointPanel);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            Grid metrics = new Grid { Margin = new Thickness(0, 8, 0, 14) };
            for (int i = 0; i < 4; i++)
                metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            latencyValue = AddMetricCard(metrics, 0, "延迟", "--", "ms", BlueColor);
            jitterValue = AddMetricCard(metrics, 1, "抖动", "--", "ms", Color.FromRgb(167, 139, 250));
            downloadValue = AddMetricCard(metrics, 2, "下载", "--", "Mbps", AccentColor);
            uploadValue = AddMetricCard(metrics, 3, "上传", "--", "Mbps", Color.FromRgb(251, 191, 36));
            Grid.SetRow(metrics, 1);
            root.Children.Add(metrics);

            Border actionPanel = new Border
            {
                Background = Brush(PanelColor),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(20, 16, 20, 16),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid actionGrid = new Grid();
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            actionGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            actionGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

            statusText = Text("准备就绪", 15, FontWeights.SemiBold, Brushes.White);
            actionGrid.Children.Add(statusText);
            liveRateText = Text("点击“开始测速”检查当前连接", 12, FontWeights.Normal, Brush(MutedColor));
            Grid.SetRow(liveRateText, 1);
            actionGrid.Children.Add(liveRateText);

            startButton = ActionButton("开始测速", Brush(AccentColor), Brush(Color.FromRgb(5, 35, 26)));
            startButton.Click += StartClicked;
            Grid.SetColumn(startButton, 1);
            Grid.SetRowSpan(startButton, 2);
            actionGrid.Children.Add(startButton);

            cancelButton = ActionButton("取消", Brush(Color.FromRgb(48, 58, 78)), Brushes.White);
            cancelButton.Margin = new Thickness(12, 0, 0, 0);
            cancelButton.IsEnabled = false;
            cancelButton.Click += delegate { if (cancellation != null) cancellation.Cancel(); };
            Grid.SetColumn(cancelButton, 2);
            Grid.SetRowSpan(cancelButton, 2);
            actionGrid.Children.Add(cancelButton);

            progressBar = new ProgressBar
            {
                Height = 4,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Foreground = Brush(AccentColor),
                Background = Brush(Color.FromRgb(40, 51, 72)),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetRow(progressBar, 1);
            Grid.SetColumnSpan(progressBar, 3);
            progressBar.Margin = new Thickness(0, 0, 0, -10);
            actionGrid.Children.Add(progressBar);
            actionPanel.Child = actionGrid;
            Grid.SetRow(actionPanel, 2);
            root.Children.Add(actionPanel);

            Border historyPanel = new Border
            {
                Background = Brush(PanelColor),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18, 14, 18, 12)
            };
            Grid historyGrid = new Grid();
            historyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            historyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            historyGrid.Children.Add(Text("最近测试", 15, FontWeights.SemiBold, Brushes.White));

            historyList = new ListView
            {
                Background = Brushes.Transparent,
                Foreground = Brush(Color.FromRgb(222, 231, 245)),
                BorderThickness = new Thickness(0),
                FontSize = 12
            };
            GridView historyView = new GridView();
            historyView.Columns.Add(new GridViewColumn { Header = "时间", DisplayMemberBinding = new System.Windows.Data.Binding("When"), Width = 180 });
            historyView.Columns.Add(new GridViewColumn { Header = "延迟", DisplayMemberBinding = new System.Windows.Data.Binding("Latency"), Width = 130 });
            historyView.Columns.Add(new GridViewColumn { Header = "抖动", DisplayMemberBinding = new System.Windows.Data.Binding("Jitter"), Width = 130 });
            historyView.Columns.Add(new GridViewColumn { Header = "下载", DisplayMemberBinding = new System.Windows.Data.Binding("Download"), Width = 150 });
            historyView.Columns.Add(new GridViewColumn { Header = "上传", DisplayMemberBinding = new System.Windows.Data.Binding("Upload"), Width = 150 });
            historyList.View = historyView;
            Grid.SetRow(historyList, 1);
            historyGrid.Children.Add(historyList);
            historyPanel.Child = historyGrid;
            Grid.SetRow(historyPanel, 3);
            root.Children.Add(historyPanel);

            TextBlock footer = Text("默认通过 Cloudflare 边缘节点测试；你可以替换为兼容的自建节点。", 11, FontWeights.Normal, Brush(MutedColor));
            footer.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetRow(footer, 4);
            root.Children.Add(footer);

            Content = root;
            LoadHistory();
        }

        private TextBlock AddMetricCard(Grid parent, int column, string label, string initial, string unit, Color accent)
        {
            Border card = new Border
            {
                Background = Brush(PanelColor),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18),
                Margin = new Thickness(column == 0 ? 0 : 7, 0, column == 3 ? 0 : 7, 0)
            };
            Grid cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            TextBlock labelBlock = Text(label, 13, FontWeights.Medium, Brush(MutedColor));
            cardGrid.Children.Add(labelBlock);
            StackPanel numberRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock value = Text(initial, 36, FontWeights.SemiBold, Brush(accent));
            TextBlock unitBlock = Text(unit, 12, FontWeights.Normal, Brush(MutedColor));
            unitBlock.Margin = new Thickness(7, 19, 0, 0);
            numberRow.Children.Add(value);
            numberRow.Children.Add(unitBlock);
            Grid.SetRow(numberRow, 1);
            cardGrid.Children.Add(numberRow);
            card.Child = cardGrid;
            Grid.SetColumn(card, column);
            parent.Children.Add(card);
            return value;
        }

        private static Button ActionButton(string text, Brush background, Brush foreground)
        {
            return new Button
            {
                Content = text,
                Height = 48,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = background,
                Foreground = foreground,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private static TextBlock Text(string value, double size, FontWeight weight, Brush color)
        {
            return new TextBlock { Text = value, FontSize = size, FontWeight = weight, Foreground = color };
        }

        private static SolidColorBrush Brush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private async void StartClicked(object sender, RoutedEventArgs e)
        {
            Uri endpoint;
            if (!Uri.TryCreate(endpointBox.Text.Trim(), UriKind.Absolute, out endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show(this, "请输入有效的 HTTP 或 HTTPS 测速服务地址。", "地址无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            startButton.IsEnabled = false;
            cancelButton.IsEnabled = true;
            endpointBox.IsEnabled = false;
            latencyValue.Text = jitterValue.Text = downloadValue.Text = uploadValue.Text = "--";
            progressBar.Value = 0;
            cancellation = new CancellationTokenSource();

            Progress<TestProgress> progress = new Progress<TestProgress>(delegate(TestProgress item)
            {
                progressBar.Value = item.Percent;
                statusText.Text = item.Status;
                liveRateText.Text = item.Detail;
                if (item.Latency >= 0) latencyValue.Text = item.Latency.ToString("0.0", CultureInfo.InvariantCulture);
                if (item.Jitter >= 0) jitterValue.Text = item.Jitter.ToString("0.0", CultureInfo.InvariantCulture);
                if (item.Download >= 0) downloadValue.Text = item.Download.ToString("0.0", CultureInfo.InvariantCulture);
                if (item.Upload >= 0) uploadValue.Text = item.Upload.ToString("0.0", CultureInfo.InvariantCulture);
            });

            try
            {
                SpeedTestResult result = await SpeedTestEngine.RunAsync(endpoint, progress, cancellation.Token);
                latencyValue.Text = result.LatencyMs.ToString("0.0", CultureInfo.InvariantCulture);
                jitterValue.Text = result.JitterMs.ToString("0.0", CultureInfo.InvariantCulture);
                downloadValue.Text = result.DownloadMbps.ToString("0.0", CultureInfo.InvariantCulture);
                uploadValue.Text = result.UploadMbps.ToString("0.0", CultureInfo.InvariantCulture);
                statusText.Text = "测试完成 · " + result.Grade;
                liveRateText.Text = "结果已保存到本机历史记录";
                progressBar.Value = 100;
                AddHistory(result);
            }
            catch (OperationCanceledException)
            {
                statusText.Text = "测试已取消";
                liveRateText.Text = "可以重新开始测试";
            }
            catch (Exception ex)
            {
                statusText.Text = "测试失败";
                liveRateText.Text = FriendlyError(ex);
            }
            finally
            {
                if (cancellation != null) cancellation.Dispose();
                cancellation = null;
                startButton.IsEnabled = true;
                cancelButton.IsEnabled = false;
                endpointBox.IsEnabled = true;
            }
        }

        private static string FriendlyError(Exception ex)
        {
            Exception current = ex;
            while (current != null)
            {
                string message = current.Message ?? "";
                if (message.IndexOf("凭据", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("credentials", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "系统 TLS 组件不可用；可检查 Windows 证书服务，或临时改用 HTTP 节点。";
                current = current.InnerException;
            }
            if (ex is HttpRequestException) return "无法连接测速节点，请检查网络或更换服务地址。";
            return ex.Message.Length > 90 ? ex.Message.Substring(0, 90) + "…" : ex.Message;
        }

        private void AddHistory(SpeedTestResult result)
        {
            HistoryItem item = HistoryItem.FromResult(result);
            historyList.Items.Insert(0, item);
            while (historyList.Items.Count > 12) historyList.Items.RemoveAt(historyList.Items.Count - 1);

            try
            {
                string path = HistoryPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                List<string> lines = new List<string>();
                foreach (HistoryItem history in historyList.Items)
                    lines.Add(history.ToStorageLine());
                File.WriteAllLines(path, lines.ToArray());
            }
            catch { }
        }

        private void LoadHistory()
        {
            try
            {
                string path = HistoryPath();
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path).Take(12))
                {
                    HistoryItem item = HistoryItem.FromStorageLine(line);
                    if (item != null) historyList.Items.Add(item);
                }
            }
            catch { }
        }

        private static string HistoryPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetPulse", "history.tsv");
        }

        public void ApplyDemoData()
        {
            latencyValue.Text = "18.6";
            jitterValue.Text = "2.1";
            downloadValue.Text = "326.8";
            uploadValue.Text = "78.4";
            statusText.Text = "测试完成 · 网络优秀";
            liveRateText.Text = "结果已保存到本机历史记录";
            progressBar.Value = 100;
            historyList.Items.Clear();
            historyList.Items.Add(new HistoryItem { When = "今天 09:42", Latency = "18.6 ms", Jitter = "2.1 ms", Download = "326.8 Mbps", Upload = "78.4 Mbps" });
            historyList.Items.Add(new HistoryItem { When = "昨天 21:16", Latency = "24.3 ms", Jitter = "3.8 ms", Download = "281.5 Mbps", Upload = "72.9 Mbps" });
        }

        public void RenderToPng(string path)
        {
            WindowState = WindowState.Normal;
            Width = 1080;
            Height = 760;
            UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
    }

    public sealed class TestProgress
    {
        public int Percent = 0;
        public string Status = "";
        public string Detail = "";
        public double Latency = -1;
        public double Jitter = -1;
        public double Download = -1;
        public double Upload = -1;
    }

    public sealed class SpeedTestResult
    {
        public DateTime CompletedAt;
        public double LatencyMs;
        public double JitterMs;
        public double DownloadMbps;
        public double UploadMbps;

        public string Grade
        {
            get
            {
                if (LatencyMs < 30 && JitterMs < 8 && DownloadMbps >= 100) return "网络优秀";
                if (LatencyMs < 60 && JitterMs < 15 && DownloadMbps >= 30) return "网络良好";
                if (LatencyMs < 120 && DownloadMbps >= 10) return "网络一般";
                return "建议检查连接";
            }
        }
    }

    public sealed class HistoryItem
    {
        public string When { get; set; }
        public string Latency { get; set; }
        public string Jitter { get; set; }
        public string Download { get; set; }
        public string Upload { get; set; }

        public static HistoryItem FromResult(SpeedTestResult result)
        {
            return new HistoryItem
            {
                When = result.CompletedAt.ToString("yyyy-MM-dd HH:mm"),
                Latency = result.LatencyMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms",
                Jitter = result.JitterMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms",
                Download = result.DownloadMbps.ToString("0.0", CultureInfo.InvariantCulture) + " Mbps",
                Upload = result.UploadMbps.ToString("0.0", CultureInfo.InvariantCulture) + " Mbps"
            };
        }

        public string ToStorageLine()
        {
            return string.Join("\t", new string[] { When, Latency, Jitter, Download, Upload });
        }

        public static HistoryItem FromStorageLine(string line)
        {
            string[] parts = line.Split('\t');
            if (parts.Length != 5) return null;
            return new HistoryItem { When = parts[0], Latency = parts[1], Jitter = parts[2], Download = parts[3], Upload = parts[4] };
        }
    }

    public static class SpeedTestEngine
    {
        private const int DownloadBytesPerRequest = 10000000;
        private const int UploadBytesPerRequest = 4000000;

        public static async Task<SpeedTestResult> RunAsync(Uri baseUri, IProgress<TestProgress> progress, CancellationToken token)
        {
            using (HttpClient client = CreateClient())
            {
                Uri downloadUri = Endpoint(baseUri, "__down");
                Uri uploadUri = Endpoint(baseUri, "__up");

                Report(progress, 2, "正在连接测速节点", "建立安全连接…", -1, -1, -1, -1);
                await DownloadOnce(client, downloadUri, 0, token);

                List<double> samples = new List<double>();
                for (int i = 0; i < 8; i++)
                {
                    token.ThrowIfCancellationRequested();
                    Stopwatch watch = Stopwatch.StartNew();
                    await DownloadOnce(client, downloadUri, 0, token);
                    watch.Stop();
                    samples.Add(watch.Elapsed.TotalMilliseconds);
                    double currentLatency = samples.Average();
                    double currentJitter = CalculateJitter(samples);
                    Report(progress, 5 + (i + 1) * 3, "正在检测延迟", string.Format("第 {0}/8 次 · {1:0.0} ms", i + 1, samples[i]), currentLatency, currentJitter, -1, -1);
                }

                double latency = samples.Average();
                double jitter = CalculateJitter(samples);
                double download = await MeasureDownload(client, downloadUri, progress, latency, jitter, token);
                double upload = await MeasureUpload(client, uploadUri, progress, latency, jitter, download, token);

                return new SpeedTestResult
                {
                    CompletedAt = DateTime.Now,
                    LatencyMs = latency,
                    JitterMs = jitter,
                    DownloadMbps = download,
                    UploadMbps = upload
                };
            }
        }

        public static async Task<string> RunSmokeAsync(Uri baseUri, CancellationToken token)
        {
            using (HttpClient client = CreateClient())
            {
                Uri downloadUri = Endpoint(baseUri, "__down");
                Uri uploadUri = Endpoint(baseUri, "__up");
                Stopwatch watch = Stopwatch.StartNew();
                long downloaded = await DownloadOnce(client, downloadUri, 250000, token);
                watch.Stop();
                using (StreamContent content = new StreamContent(new ZeroStream(65536)))
                {
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    content.Headers.ContentLength = 65536;
                    using (HttpResponseMessage response = await client.PostAsync(uploadUri, content, token))
                    {
                        response.EnsureSuccessStatusCode();
                    }
                }
                return string.Format(CultureInfo.InvariantCulture, "OK download_bytes={0} download_ms={1:0.0} upload_bytes=65536", downloaded, watch.Elapsed.TotalMilliseconds);
            }
        }

        private static HttpClient CreateClient()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.None;
            handler.UseProxy = true;
            HttpClient client = new HttpClient(handler);
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NetPulse/0.1 Windows");
            client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            return client;
        }

        private static Uri Endpoint(Uri baseUri, string path)
        {
            string root = baseUri.AbsoluteUri.TrimEnd('/') + "/";
            return new Uri(new Uri(root), path);
        }

        private static async Task<long> DownloadOnce(HttpClient client, Uri uri, int bytes, CancellationToken token)
        {
            Uri requestUri = new Uri(uri.AbsoluteUri + "?bytes=" + bytes.ToString(CultureInfo.InvariantCulture) + "&r=" + Guid.NewGuid().ToString("N"));
            using (HttpResponseMessage response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                using (Stream stream = await response.Content.ReadAsStreamAsync())
                {
                    byte[] buffer = new byte[65536];
                    long total = 0;
                    while (true)
                    {
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                        if (read == 0) break;
                        total += read;
                    }
                    return total;
                }
            }
        }

        private static async Task<double> MeasureDownload(HttpClient client, Uri uri, IProgress<TestProgress> progress, double latency, double jitter, CancellationToken token)
        {
            const int durationSeconds = 7;
            Stopwatch watch = Stopwatch.StartNew();
            long totalBytes = 0;
            List<Task> workers = new List<Task>();

            for (int worker = 0; worker < 4; worker++)
            {
                workers.Add(Task.Run(async delegate
                {
                    while (watch.Elapsed.TotalSeconds < durationSeconds)
                    {
                        long bytes = await DownloadOnce(client, uri, DownloadBytesPerRequest, token);
                        Interlocked.Add(ref totalBytes, bytes);
                        double seconds = Math.Max(0.05, watch.Elapsed.TotalSeconds);
                        double rate = Interlocked.Read(ref totalBytes) * 8.0 / seconds / 1000000.0;
                        int pct = 30 + (int)Math.Min(34, seconds / durationSeconds * 34);
                        Report(progress, pct, "正在测试下载速度", string.Format("实时 {0:0.0} Mbps · 4 路并发", rate), latency, jitter, rate, -1);
                    }
                }, token));
            }

            await Task.WhenAll(workers.ToArray());
            watch.Stop();
            return totalBytes * 8.0 / Math.Max(0.05, watch.Elapsed.TotalSeconds) / 1000000.0;
        }

        private static async Task<double> MeasureUpload(HttpClient client, Uri uri, IProgress<TestProgress> progress, double latency, double jitter, double download, CancellationToken token)
        {
            const int durationSeconds = 7;
            Stopwatch watch = Stopwatch.StartNew();
            long totalBytes = 0;
            List<Task> workers = new List<Task>();

            for (int worker = 0; worker < 3; worker++)
            {
                workers.Add(Task.Run(async delegate
                {
                    while (watch.Elapsed.TotalSeconds < durationSeconds)
                    {
                        using (StreamContent content = new StreamContent(new ZeroStream(UploadBytesPerRequest)))
                        {
                            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                            content.Headers.ContentLength = UploadBytesPerRequest;
                            using (HttpResponseMessage response = await client.PostAsync(uri, content, token))
                            {
                                response.EnsureSuccessStatusCode();
                                Interlocked.Add(ref totalBytes, UploadBytesPerRequest);
                            }
                        }
                        double seconds = Math.Max(0.05, watch.Elapsed.TotalSeconds);
                        double rate = Interlocked.Read(ref totalBytes) * 8.0 / seconds / 1000000.0;
                        int pct = 66 + (int)Math.Min(33, seconds / durationSeconds * 33);
                        Report(progress, pct, "正在测试上传速度", string.Format("实时 {0:0.0} Mbps · 3 路并发", rate), latency, jitter, download, rate);
                    }
                }, token));
            }

            await Task.WhenAll(workers.ToArray());
            watch.Stop();
            return totalBytes * 8.0 / Math.Max(0.05, watch.Elapsed.TotalSeconds) / 1000000.0;
        }

        private static double CalculateJitter(List<double> samples)
        {
            if (samples.Count < 2) return 0;
            double total = 0;
            for (int i = 1; i < samples.Count; i++) total += Math.Abs(samples[i] - samples[i - 1]);
            return total / (samples.Count - 1);
        }

        private static void Report(IProgress<TestProgress> progress, int percent, string status, string detail, double latency, double jitter, double download, double upload)
        {
            progress.Report(new TestProgress { Percent = percent, Status = status, Detail = detail, Latency = latency, Jitter = jitter, Download = download, Upload = upload });
        }
    }

    public sealed class ZeroStream : Stream
    {
        private readonly long length;
        private long position;

        public ZeroStream(long length) { this.length = length; }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return length; } }
        public override long Position { get { return position; } set { position = value; } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = (int)Math.Min(count, length - position);
            if (available <= 0) return 0;
            Array.Clear(buffer, offset, available);
            position += available;
            return available;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin) position = offset;
            else if (origin == SeekOrigin.Current) position += offset;
            else position = length + offset;
            return position;
        }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
}
