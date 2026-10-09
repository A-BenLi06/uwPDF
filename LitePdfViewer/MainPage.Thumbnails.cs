using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace LitePdfViewer
{
    public sealed partial class MainPage
    {
        private const double ThumbnailRowHeight = 156;

        private void ResetThumbnails()
        {
            foreach (var item in thumbnailItems.Values) ReleaseThumbnail(item);
            thumbnailItems.Clear();
            selectedThumbIndex = -1;
            ThumbnailPanel.Children.Clear();
            ThumbnailPanel.Height = 0;
            ThumbnailScroller.ChangeView(null, 0, null, true);
        }

        private void UpdateThumbnailWindow()
        {
            if (document == null || pageViews.Count == 0) return;
            ThumbnailPanel.Height = document.PageCount * ThumbnailRowHeight;
            if (!thumbsRequested)
            {
                foreach (var item in thumbnailItems.Values) ReleaseThumbnail(item);
                thumbnailItems.Clear();
                ThumbnailPanel.Children.Clear();
                return;
            }
            var first = Math.Max(0, (int)(ThumbnailScroller.VerticalOffset / ThumbnailRowHeight) - 2);
            var last = Math.Min((int)document.PageCount - 1,
                (int)((ThumbnailScroller.VerticalOffset + Math.Max(1, ThumbnailScroller.ViewportHeight)) / ThumbnailRowHeight) + 2);
            var remove = new List<int>();
            foreach (var pair in thumbnailItems)
                if (pair.Key < first || pair.Key > last) remove.Add(pair.Key);
            foreach (var index in remove)
            {
                var item = thumbnailItems[index];
                ReleaseThumbnail(item);
                ThumbnailPanel.Children.Remove(item.Button);
                thumbnailItems.Remove(index);
            }
            for (var i = first; i <= last; i++)
                if (!thumbnailItems.ContainsKey(i)) AddThumbnailPlaceholder(i);
            if (selectedThumbIndex < 0) selectedThumbIndex = (int)pageIndex;
            WakeRenderer();
        }

        private void AddThumbnailPlaceholder(int index)
        {
            var image = new Image { Width = ThumbWidth, Height = Math.Min(112, ThumbWidth * pageViews[index].Aspect), Stretch = Stretch.Uniform };
            var card = new Border { Child = image, Background = PageFillBrush,
                BorderBrush = index == (int)pageIndex ? ThumbSelectedBorderBrush : ThumbIdleBorderBrush,
                BorderThickness = new Thickness(1) };
            var number = new TextBlock { Text = (index + 1).ToString(CultureInfo.InvariantCulture),
                FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0),
                Foreground = index == (int)pageIndex ? ThumbSelectedTextBrush : ThumbIdleTextBrush };
            var stack = new StackPanel();
            stack.Children.Add(card);
            stack.Children.Add(number);
            var button = new Button { Content = stack, Tag = (uint)index, Style = (Style)Resources["ThumbCardStyle"],
                Width = 94, Height = 148 };
            button.Click += ThumbnailButton_Click;
            AutomationProperties.SetName(button, "Page thumbnail " + (index + 1).ToString(CultureInfo.InvariantCulture));
            Canvas.SetTop(button, index * ThumbnailRowHeight);
            thumbnailItems.Add(index, new ThumbnailItem { Button = button, Card = card, Number = number, Image = image });
            ThumbnailPanel.Children.Add(button);
        }

        private static void ReleaseThumbnail(ThumbnailItem item)
        {
            item.Image.Source = null;
            if (item.Raster != null) { item.Raster.Dispose(); item.Raster = null; }
            item.IsRendered = false;
        }

        private int FindNextThumbnailIndex()
        {
            if (memoryConstrained || !thumbsRequested || DateTime.UtcNow < thumbnailSettledAt) return -1;
            if (thumbnailItems.Count == 0) UpdateThumbnailWindow();
            var center = (int)((ThumbnailScroller.VerticalOffset + ThumbnailScroller.ViewportHeight / 2) / ThumbnailRowHeight);
            var best = -1; var distance = int.MaxValue;
            foreach (var pair in thumbnailItems)
            {
                if (pair.Value.IsRendered || pair.Value.RenderFailed) continue;
                var d = Math.Abs(pair.Key - center);
                if (d < distance) { best = pair.Key; distance = d; }
            }
            return best;
        }

        private void ThumbnailScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            thumbnailSettledAt = e.IsIntermediate ? DateTime.UtcNow.AddMilliseconds(120) : DateTime.MinValue;
            if (!e.IsIntermediate) UpdateThumbnailWindow();
            WakeRenderer();
        }

        private void ThumbnailScroller_SizeChanged(object sender, SizeChangedEventArgs e) { UpdateThumbnailWindow(); }

        private void ThumbnailButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null && button.Tag != null) GoToPage((uint)button.Tag);
        }

        private void UpdateThumbnailSelection()
        {
            var target = (int)pageIndex;
            if (target == selectedThumbIndex) return;
            ThumbnailItem previous, current;
            if (thumbnailItems.TryGetValue(selectedThumbIndex, out previous))
            {
                previous.Card.BorderBrush = ThumbIdleBorderBrush;
                previous.Number.Foreground = ThumbIdleTextBrush;
            }
            selectedThumbIndex = target;
            if (thumbnailItems.TryGetValue(target, out current))
            {
                current.Card.BorderBrush = ThumbSelectedBorderBrush;
                current.Number.Foreground = ThumbSelectedTextBrush;
            }
            if (IsScrollSettled()) FollowCurrentThumbnail();
        }

        private void FollowCurrentThumbnail()
        {
            if (thumbsRequested)
            {
                var top = pageIndex * ThumbnailRowHeight;
                if (top < ThumbnailScroller.VerticalOffset || top + ThumbnailRowHeight > ThumbnailScroller.VerticalOffset + ThumbnailScroller.ViewportHeight)
                    ThumbnailScroller.ChangeView(null, top, null, true);
            }
        }
    }
}
