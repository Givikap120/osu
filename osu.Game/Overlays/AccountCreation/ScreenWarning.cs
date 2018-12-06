// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Overlays.Settings;
using osu.Game.Screens.Menu;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.AccountCreation
{
    public class ScreenWarning : AccountCreationScreen
    {
        private OsuTextFlowContainer multiAccountExplanationText;
        private LinkFlowContainer furtherAssistance;

        private const string help_centre_url = "/help/wiki/Help_Centre#login";

        [BackgroundDependencyLoader(true)]
        private void load(OsuColour colours, APIAccess api, OsuGame game)
        {
            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Padding = new MarginPadding(20),
                Spacing = new Vector2(0, 5),
                Children = new Drawable[]
                {
                    new OsuLogo
                    {
                        Scale = new Vector2(0.1f),
                        Margin = new MarginPadding { Top = 500, Bottom = 300 },
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Triangles = false,
                        BeatMatching = false,
                    },
                    new OsuSpriteText
                    {
                        TextSize = 28,
                        Font = "Exo2.0-Light",
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Colour = Color4.Red,
                        Text = "Warning! 注意！",
                    },
                    multiAccountExplanationText = new OsuTextFlowContainer(cp => { cp.TextSize = 12; })
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y
                    },
                    new SettingsButton
                    {
                        Text = "Help, I can't access my account!",
                        Margin = new MarginPadding { Top = 50 },
                        Action = () => game?.OpenUrlExternally(help_centre_url)
                    },
                    new DangerousSettingsButton
                    {
                        Text = "I understand. This account isn't for me.",
                        Action = () => Push(new ScreenEntry())
                    },
                    furtherAssistance = new LinkFlowContainer(cp => { cp.TextSize = 12; })
                    {
                        Margin = new MarginPadding { Top = 20 },
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        AutoSizeAxes = Axes.Both
                    },
                }
            };

            multiAccountExplanationText.AddText("Are you ");
            multiAccountExplanationText.AddText(api.ProvidedUsername, cp => cp.Colour = colours.BlueLight);
            multiAccountExplanationText.AddText("? osu! has a policy of ");
            multiAccountExplanationText.AddText("one account per person!", cp => cp.Colour = colours.Yellow);
            multiAccountExplanationText.AddText(" Please be aware that creating more than one account per person may result in ");
            multiAccountExplanationText.AddText("permanent deactivation of accounts", cp => cp.Colour = colours.Yellow);
            multiAccountExplanationText.AddText(".");

            furtherAssistance.AddText("Need further assistance? Contact us via our ");
            furtherAssistance.AddLink("support system", help_centre_url);
            furtherAssistance.AddText(".");
        }
    }
}
