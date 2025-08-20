using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FacebookChecker
{
    public partial class MainForm : Form
    {
        private CancellationTokenSource? cts;
        private int liveCount = 0, deadCount = 0, errorCount = 0;
        private readonly Random sharedRandom = new Random();

        private readonly string[] userAgents =
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/114.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/16.0 Safari/605.1.15",
            "Mozilla/5.0 (Linux; Android 10; SM-G975F) AppleWebKit/537.36 Chrome/111.0 Mobile Safari/537.36"
        };

        public MainForm()
        {
            InitializeComponent();
        }

        private void btnBrowseList_Click(object sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog { Filter = "Text Files|*.txt" };
            if (ofd.ShowDialog() == DialogResult.OK)
                txtListFile.Text = ofd.FileName;
        }

        private void btnBrowseCookies_Click(object sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog { Filter = "Text Files|*.txt" };
            if (ofd.ShowDialog() == DialogResult.OK)
                txtCookiesFile.Text = ofd.FileName;
        }

        private void btnBrowseProxies_Click(object sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog { Filter = "Text Files|*.txt" };
            if (ofd.ShowDialog() == DialogResult.OK)
                txtProxiesFile.Text = ofd.FileName;
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (!File.Exists(txtListFile.Text))
            {
                MessageBox.Show("File list.txt harus dipilih/ada.", "Validasi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnStart.Enabled = false;
            btnStop.Enabled = true;
            btnOpenLive.Enabled = false;
            btnOpenDead.Enabled = false;
            logBox.Clear();
            progressBar.Value = 0;
            liveCount = deadCount = errorCount = 0;
            UpdateStatus();

            cts = new CancellationTokenSource();

            string[] lists = File.ReadAllLines(txtListFile.Text)
                                  .Select(s => s.Trim())
                                  .Where(s => !string.IsNullOrWhiteSpace(s))
                                  .ToArray();
            string cookies = "";
            if (File.Exists(txtCookiesFile.Text))
            {
                cookies = File.ReadAllText(txtCookiesFile.Text).Trim();
            }

            string[] proxies = new string[0];
            if (File.Exists(txtProxiesFile.Text))
            {
                proxies = File.ReadAllLines(txtProxiesFile.Text)
                               .Select(s => s.Trim())
                               .Where(s => !string.IsNullOrWhiteSpace(s))
                               .ToArray();
            }

            int batchSize = (int)numBatch.Value;
            int delayMs = (int)numDelay.Value * 1000;
            bool enableJitter = chkJitter.Checked;
            int maxRetries = (int)numRetries.Value;

            int total = lists.Length;
            int processed = 0;

            foreach (var batch in Batch(lists, batchSize))
            {
                if (cts.IsCancellationRequested) break;

                var tasks = new List<Task>();
                foreach (string id in batch)
                {
                    tasks.Add(ProcessWithRetriesAsync(id, cookies, proxies, maxRetries, () =>
                    {
                        Interlocked.Increment(ref processed);
                        UpdateProgress(processed, total);
                        UpdateStatus();
                    }, cts.Token));
                }

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException) { /* ignore */ }

                if (processed < total && !cts.IsCancellationRequested)
                {
                    int jitter = enableJitter ? sharedRandom.Next(500, 1500) : 0;
                    try
                    {
                        await Task.Delay(delayMs + jitter, cts.Token);
                    }
                    catch (OperationCanceledException) { /* ignore */ }
                }
            }

            btnStart.Enabled = true;
            btnStop.Enabled = false;
            btnOpenLive.Enabled = File.Exists("live.txt");
            btnOpenDead.Enabled = File.Exists("dead.txt");
        }

        private async Task ProcessWithRetriesAsync(string id, string cookies, string[] proxies, int maxRetries, Action onFinally, CancellationToken token)
        {
            int attempt = 0;
            Exception? lastEx = null;
            while (attempt <= maxRetries && !token.IsCancellationRequested)
            {
                try
                {
                    string proxy = proxies.Length > 0 ? proxies[sharedRandom.Next(proxies.Length)] : "";
                    using var handler = CreateHandlerForProxy(proxy);
                    using var client = new HttpClient(handler);

                    await ProcessIdAsync(client, id, cookies, token);
                    lastEx = null;
                    break; // success
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    attempt++;
                    AppendLog($"[RETRY] {id} attempt {attempt} - {ex.Message}");
                    // exponential backoff with jitter
                    int backoff = (int)(Math.Pow(2, attempt) * 500) + sharedRandom.Next(0, 500);
                    try { await Task.Delay(backoff, token); } catch { }
                }
            }

            if (lastEx != null)
            {
                AppendLog($"[ERROR] {id} - after {maxRetries} retries: {lastEx.Message}");
                Interlocked.Increment(ref errorCount);
            }

            onFinally();
        }

        private HttpClientHandler CreateHandlerForProxy(string proxy)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            if (!string.IsNullOrEmpty(proxy))
            {
                try
                {
                    var uri = new Uri(proxy);
                    handler.Proxy = new WebProxy(uri);
                    handler.UseProxy = true;
                }
                catch
                {
                    // ignore invalid proxy format
                }
            }

            return handler;
        }

        private async Task ProcessIdAsync(HttpClient client, string id, string cookies, CancellationToken token)
        {
            string url = $"https://www.facebook.com/{id}/reviews/";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // random UA
            string ua = userAgents[sharedRandom.Next(userAgents.Length)];
            request.Headers.TryAddWithoutValidation("User-Agent", ua);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            if (!string.IsNullOrEmpty(cookies))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookies);
            }

            using var response = await client.SendAsync(request, token);
            string body = await response.Content.ReadAsStringAsync(token);

            if (body.Contains("{\\\"title\\\":{\\\"text\\\":\\\"") || body.Contains("{\"title\":{\"text\":\""))
            {
                AppendLog($"[LIVE] {id}");
                Interlocked.Increment(ref liveCount);
                File.AppendAllText("live.txt", id + Environment.NewLine);
            }
            else
            {
                AppendLog($"[DEAD] {id}");
                Interlocked.Increment(ref deadCount);
                File.AppendAllText("dead.txt", id + Environment.NewLine);
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            cts?.Cancel();
            AppendLog("⛔ Dihentikan oleh pengguna.");
            btnStart.Enabled = true;
            btnStop.Enabled = false;
        }

        private void btnOpenLive_Click(object sender, EventArgs e)
        {
            if (File.Exists("live.txt"))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "live.txt") { UseShellExecute = false });
        }

        private void btnOpenDead_Click(object sender, EventArgs e)
        {
            if (File.Exists("dead.txt"))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "dead.txt") { UseShellExecute = false });
        }

        private void AppendLog(string text)
        {
            if (logBox.InvokeRequired)
            {
                logBox.Invoke(new Action(() => logBox.AppendText(text + Environment.NewLine)));
            }
            else
            {
                logBox.AppendText(text + Environment.NewLine);
            }
            try { File.AppendAllText("log.txt", text + Environment.NewLine); } catch { }
        }

        private void UpdateProgress(int current, int total)
        {
            int pct = Math.Max(0, Math.Min(100, (int)Math.Round(current / (double)total * 100)));
            if (progressBar.InvokeRequired)
            {
                progressBar.Invoke(new Action(() => progressBar.Value = pct));
            }
            else
            {
                progressBar.Value = pct;
            }
        }

        private void UpdateStatus()
        {
            string status = $"LIVE: {liveCount} | DEAD: {deadCount} | ERROR: {errorCount}";
            if (lblStatus.InvokeRequired)
            {
                lblStatus.Invoke(new Action(() => lblStatus.Text = status));
            }
            else
            {
                lblStatus.Text = status;
            }
        }

        // helper batch
        static IEnumerable<IEnumerable<T>> Batch<T>(IEnumerable<T> source, int size)
        {
            T[]? bucket = null;
            var count = 0;

            foreach (var item in source)
            {
                bucket ??= new T[size];
                bucket[count++] = item;
                if (count != size) continue;
                yield return bucket;
                bucket = null;
                count = 0;
            }
            if (bucket != null && count > 0)
                yield return bucket.Take(count);
        }
    }
}
