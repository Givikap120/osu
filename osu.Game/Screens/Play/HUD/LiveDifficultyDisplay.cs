// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Screens.Play.HUD
{
    public abstract partial class LiveDifficultyDisplay : Container
    {
        public bool UsesFixedAnchor { get; set; }

        [Resolved]
        protected ScoreProcessor? ScoreProcessor { get; private set; }

        [Resolved]
        protected GameplayState? GameplayState { get; private set; }

        [CanBeNull]
        private List<TimedDifficultyAttributes>? timedAttributes;

        private readonly CancellationTokenSource loadCancellationSource = new CancellationTokenSource();

        private JudgementResult? lastJudgement;

        protected Mod[]? ClonedMods;

        public LiveDifficultyDisplay()
        {
            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(BeatmapDifficultyCache difficultyCache)
        {
            if (GameplayState != null)
            {
                ClonedMods = GameplayState.Mods.Select(m => m.DeepClone()).ToArray();

                var gameplayWorkingBeatmap = new GameplayWorkingBeatmap(GameplayState.Beatmap);
                difficultyCache.GetTimedDifficultyAttributesAsync(gameplayWorkingBeatmap, GameplayState.Ruleset, ClonedMods, loadCancellationSource.Token)
                               .ContinueWith(task => Schedule(() =>
                               {
                                   timedAttributes = task.GetResultSafely();

                                   IsValid = true;

                                   if (lastJudgement != null)
                                       OnJudgementChanged(lastJudgement);
                               }), TaskContinuationOptions.OnlyOnRanToCompletion);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (ScoreProcessor != null)
            {
                ScoreProcessor.NewJudgement += OnJudgementChanged;
                ScoreProcessor.JudgementReverted += OnJudgementChanged;
            }

            if (GameplayState?.LastJudgementResult.Value != null)
                OnJudgementChanged(GameplayState.LastJudgementResult.Value);
        }

        public virtual bool IsValid { get; set; }

        protected virtual void OnJudgementChanged(JudgementResult judgement)
        {
            lastJudgement = judgement;

            if (GameplayState == null || ScoreProcessor == null)
            {
                IsValid = false;
                return;
            }

            IsValid = true;
        }

        protected DifficultyAttributes? GetAttributeAtTime(JudgementResult judgement)
        {
            if (timedAttributes == null || timedAttributes.Count == 0)
                return null;

            int attribIndex = timedAttributes.BinarySearch(new TimedDifficultyAttributes(judgement.HitObject.GetEndTime(), null));
            if (attribIndex < 0)
                attribIndex = ~attribIndex - 1;

            return timedAttributes[Math.Clamp(attribIndex, 0, timedAttributes.Count - 1)].Attributes;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (ScoreProcessor != null)
            {
                ScoreProcessor.NewJudgement -= OnJudgementChanged;
                ScoreProcessor.JudgementReverted -= OnJudgementChanged;
            }

            loadCancellationSource?.Cancel();
        }

        // TODO: This class shouldn't exist, but requires breaking changes to allow DifficultyCalculator to receive an IBeatmap.
        private class GameplayWorkingBeatmap : WorkingBeatmap
        {
            private readonly IBeatmap gameplayBeatmap;

            public GameplayWorkingBeatmap(IBeatmap gameplayBeatmap)
                : base(gameplayBeatmap.BeatmapInfo, null)
            {
                this.gameplayBeatmap = gameplayBeatmap;
            }

            public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken cancellationToken)
                => gameplayBeatmap;

            protected override IBeatmap GetBeatmap() => gameplayBeatmap;

            public override Texture GetBackground() => throw new NotImplementedException();

            protected override Track GetBeatmapTrack() => throw new NotImplementedException();

            protected internal override ISkin GetSkin() => throw new NotImplementedException();

            public override Stream GetStream(string storagePath) => throw new NotImplementedException();
        }
    }
}
