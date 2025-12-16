using Microsoft.Web.WebView2.Core;
using SteamPP.Helpers;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SteamPP.Views
{
    public partial class ApiKeyAutomationWindow : Window
    {
        private const string BaseUrl = "https://manifest.morrenus.xyz";
        private const string ApiKeyPageUrl = "https://manifest.morrenus.xyz/api-keys/user";
        private const int MaxRetries = 15;
        private const int RetryDelayMs = 1000;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public string? GeneratedApiKey { get; private set; }

        public ApiKeyAutomationWindow()
        {
            InitializeComponent();
            InitializeWebView();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts.Cancel();
            _cts.Dispose();
            base.OnClosed(e);
        }

        private async void InitializeWebView()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string userDataFolder = Path.Combine(appData, "Steam++", "WebView2Data");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

                await WebView.EnsureCoreWebView2Async(env);
                WebView.CoreWebView2.Navigate(BaseUrl);
                WebView.NavigationCompleted += WebView_NavigationCompleted;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error initializing WebView2: {ex.Message}";
            }
        }

        private async void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                if (!e.IsSuccess)
                {
                    StatusText.Text = "Navigation failed.";
                    return;
                }

                if (_cts.Token.IsCancellationRequested) return;

                // Check if WebView is still valid
                if (WebView.CoreWebView2 == null) return;

                string currentUrl = WebView.Source.ToString();
                StatusText.Text = $"Current URL: {currentUrl}";

                if (IsDiscordLoginUrl(currentUrl))
                {
                    StatusText.Text = "Checking for 'Authorize' button...";
                    await AttemptClickContinueWithDiscord();
                    return;
                }

                if (currentUrl.TrimEnd('/') == BaseUrl)
                {
                    StatusText.Text = "Checking login status...";
                    await AttemptClickContinueWithDiscord();
                    return;
                }

                if (currentUrl.Contains("/api-keys/user"))
                {
                    StatusText.Text = "On API Key page. Processing...";
                    await ProcessApiKeyPage();
                }
            }
            catch (Exception)
            {
                // Ignore errors if window is closing
            }
        }

        private bool IsDiscordLoginUrl(string url)
        {
            return url.Contains("discord.com/login") || url.Contains("discord.com/oauth2");
        }

        private async Task AttemptClickContinueWithDiscord()
        {
            for (int i = 0; i < MaxRetries; i++)
            {
                if (_cts.Token.IsCancellationRequested) return;
                if (WebView.CoreWebView2 == null) return;

                string script = @"
                    (function() {
                        const allLinks = document.querySelectorAll('a, button');
                        for (const link of allLinks) {
                            const text = (link.innerText || '').toLowerCase();
                            if (text.includes('logout') || text.includes('api keys')) {
                                return 'already_logged_in';
                            }
                        }

                        const discordLoginBtn = document.querySelector('a.discord-login-btn');
                        if (discordLoginBtn) {
                            discordLoginBtn.click();
                            return 'clicked_login';
                        }

                        const scrollables = document.querySelectorAll('div[class*=""scroller""], div[style*=""overflow""]');
                        for (const el of scrollables) {
                            el.scrollTop = el.scrollHeight;
                        }
                        
                        const buttons = document.querySelectorAll('button');
                        for (const btn of buttons) {
                            const text = (btn.innerText || '').toLowerCase();
                            if (text.includes('authorize')) {
                                if (btn.disabled) return 'found_but_disabled';
                                btn.click();
                                return 'clicked_authorize';
                            }
                        }
                        
                        return 'not_found';
                    })();
                ";

                try
                {
                    string result = await WebView.ExecuteScriptAsync(script);

                    if (result.Contains("already_logged_in"))
                    {
                        StatusText.Text = "Logged in. Navigating to API keys...";
                        if (WebView.CoreWebView2 != null)
                            WebView.CoreWebView2.Navigate(ApiKeyPageUrl);
                        return;
                    }
                    else if (result.Contains("clicked_login"))
                    {
                        StatusText.Text = "Clicked 'Continue with Discord'...";
                        return;
                    }
                    else if (result.Contains("clicked_authorize"))
                    {
                        StatusText.Text = "Clicked 'Authorize'...";
                        return;
                    }
                    else if (result.Contains("found_but_disabled"))
                    {
                        StatusText.Text = "Waiting for Authorize button...";
                    }
                }
                catch (Exception)
                {
                    // WebView might be disposed or busy
                    if (_cts.Token.IsCancellationRequested) return;
                }

                try
                {
                    await Task.Delay(RetryDelayMs, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            if (!_cts.Token.IsCancellationRequested && WebView.CoreWebView2 != null && WebView.Source.ToString().TrimEnd('/') == BaseUrl)
            {
                StatusText.Text = "Navigating to API keys page...";
                WebView.CoreWebView2.Navigate(ApiKeyPageUrl);
            }
        }

        private async Task ProcessApiKeyPage()
        {
            if (_cts.Token.IsCancellationRequested) return;

            string? key = await ExtractKey();
            if (!string.IsNullOrEmpty(key))
            {
                FinishWithKey(key);
                return;
            }

            StatusText.Text = "Generating new key...";
            bool clicked = await ClickGenerateButton();

            if (clicked)
            {
                for (int i = 0; i < 10; i++)
                {
                    if (_cts.Token.IsCancellationRequested) return;

                    try
                    {
                        await Task.Delay(RetryDelayMs, _cts.Token);
                    }
                    catch (OperationCanceledException) { return; }

                    key = await ExtractKey();
                    if (!string.IsNullOrEmpty(key))
                    {
                        FinishWithKey(key);
                        return;
                    }
                }
                StatusText.Text = "Timeout waiting for key. Please copy manually.";
            }
            else
            {
                StatusText.Text = "Generate button not found. Please copy manually.";
            }
        }

        private async Task<string?> ExtractKey()
        {
            if (_cts.Token.IsCancellationRequested) return null;

            string script = @"
                (function() {
                    const newKeySpan = document.getElementById('newApiKey');
                    if (newKeySpan && newKeySpan.innerText && newKeySpan.innerText.startsWith('smm')) {
                        return newKeySpan.innerText;
                    }

                    const allElements = document.querySelectorAll('span, div, p, code');
                    for (const el of allElements) {
                        const text = el.innerText ? el.innerText.trim() : '';
                        if (text.startsWith('smm') && text.length > 20 && !text.includes(' ')) {
                             return text;
                        }
                    }
                    return null;
                })();
            ";

            try
            {
                string result = await WebView.ExecuteScriptAsync(script);
                if (result != "null" && !string.IsNullOrEmpty(result))
                {
                    try
                    {
                        return JsonSerializer.Deserialize<string>(result, JsonHelper.Options);
                    }
                    catch
                    {
                        return result.Trim('"');
                    }
                }
            }
            catch (Exception)
            {
                // Ignore errors if webview is closing
            }
            return null;
        }

        private async Task<bool> ClickGenerateButton()
        {
            if (_cts.Token.IsCancellationRequested) return false;

            string script = @"
                (function() {
                    const generateBtn = document.getElementById('generateBtn');
                    if (generateBtn) {
                        generateBtn.click();
                        return true;
                    }
                    return false;
                })();
            ";

            try
            {
                string result = await WebView.ExecuteScriptAsync(script);
                return result == "true";
            }
            catch
            {
                return false;
            }
        }

        private void FinishWithKey(string key)
        {
            if (_cts.Token.IsCancellationRequested) return;

            GeneratedApiKey = key;
            StatusText.Text = $"Success! Key found: {key}";
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WebView.CoreWebView2.CookieManager.DeleteAllCookies();
                WebView.CoreWebView2.Navigate(BaseUrl);
                StatusText.Text = "Cookies cleared. Restarting...";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error clearing cookies: {ex.Message}";
            }
        }
    }
}
