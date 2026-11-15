using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace BigWalkVRInstaller
{
    // TextBlock text with http links turned into hyperlinks, clicks bubble as Hyperlink.Click
    public static class LinkText
    {
        // trailing punctuation stays outside the link
        static readonly Regex Url = new Regex(@"https?://\S*[^\s.,!?:;)\]'""]");

        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(LinkText), new PropertyMetadata(null, OnTextChanged));

        public static string GetText(TextBlock element) => (string)element.GetValue(TextProperty);
        public static void SetText(TextBlock element, string value) => element.SetValue(TextProperty, value);

        static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var block = (TextBlock)d;
            var text = (string)e.NewValue ?? "";
            block.Inlines.Clear();
            var start = 0;
            foreach (Match match in Url.Matches(text))
            {
                // notes come from the manifest, malformed links stay plain text
                if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) continue;
                block.Inlines.Add(new Run(text.Substring(start, match.Index - start)));
                block.Inlines.Add(new Hyperlink(new Run(match.Value)) { NavigateUri = uri, Style = (Style)block.FindResource("TextLink") });
                start = match.Index + match.Length;
            }
            block.Inlines.Add(new Run(text.Substring(start)));
        }
    }
}
