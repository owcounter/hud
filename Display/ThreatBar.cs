using Owmeta.Services;
using System.Windows;
using System.Windows.Controls;

namespace Owmeta.Display
{
    /// <summary>Red line naming the enemy who counters most of your team, with the teammates he counters.</summary>
    public sealed class ThreatBar : Border
    {
        public ThreatBar()
        {
            Background = HudPalette.ThreatBand;
            Height = 80;
            Margin = new Thickness(0, 4, 0, 0);
            Visibility = Visibility.Collapsed;
        }

        public TeamThreat? Threat { get; private set; }

        public void Update(TeamThreat? threat)
        {
            Threat = threat;
            if (threat == null)
            {
                Child = null;
                Visibility = Visibility.Collapsed;
                return;
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Board.Avatar(threat.Enemy, 80));
            var text = Board.BoardText($"{HudFormat.HeroName(threat.Enemy).ToUpperInvariant()} {threat.Victims.Count}/5", 38);
            text.Margin = new Thickness(24, 0, 20, 0);
            row.Children.Add(text);
            foreach (var victim in threat.Victims)
            {
                var avatar = Board.Avatar(victim, 44);
                avatar.Margin = new Thickness(0, 0, 6, 0);
                avatar.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(avatar);
            }
            Child = row;
            Visibility = Visibility.Visible;
        }
    }
}
