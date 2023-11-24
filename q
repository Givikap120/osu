[1mdiff --git a/osu.Game.Rulesets.Osu/Difficulty/Evaluators/AimEvaluator.cs b/osu.Game.Rulesets.Osu/Difficulty/Evaluators/AimEvaluator.cs[m
[1mindex 3e6937b49e..23664bfc49 100644[m
[1m--- a/osu.Game.Rulesets.Osu/Difficulty/Evaluators/AimEvaluator.cs[m
[1m+++ b/osu.Game.Rulesets.Osu/Difficulty/Evaluators/AimEvaluator.cs[m
[36m@@ -51,7 +51,7 @@[m [mpublic static double EvaluateDifficultyOf(DifficultyHitObject current, bool with[m
             // double areaDifficulty = 180 / circleArea;[m
 [m
             double flowDifficulty = linearDifficulty * osuCurrObj.Movement.Length / osuCurrObj.StrainTime;[m
[31m-            double snapDifficulty = Math.Max(100 * areaDifficulty * osuCurrObj.Movement.Length / Math.Pow(osuCurrObj.StrainTime, 2), 0.8 * linearDifficulty * osuCurrObj.Movement.Length / osuCurrObj.StrainTime);[m
[32m+[m[32m            double snapDifficulty = Math.Max(125 * areaDifficulty * osuCurrObj.Movement.Length / Math.Pow(osuCurrObj.StrainTime, 2), linearDifficulty * osuCurrObj.Movement.Length / osuCurrObj.StrainTime);[m
 [m
             double currVelocity = osuCurrObj.Movement.Length / osuCurrObj.StrainTime;[m
             double prevVelocity = osuLastObj0.Movement.Length / osuLastObj0.StrainTime;[m
[36m@@ -66,10 +66,10 @@[m [mpublic static double EvaluateDifficultyOf(DifficultyHitObject current, bool with[m
             flowDifficulty += linearDifficulty * Math.Min(Math.Abs(currVelocity - prevVelocity), Math.Min(currVelocity, prevVelocity));[m
             flowDifficulty *= Math.Min(2, Math.Max(1, osuCurrObj.Movement.Length / (osuCurrObj.Radius * 3)));[m
 [m
[31m-            double snapFlowDifficulty = 100 * areaDifficulty * osuLastObj0.Movement.Length / Math.Pow(osuLastObj0.StrainTime, 2)[m
[32m+[m[32m            double snapFlowDifficulty = 125 * areaDifficulty * osuLastObj0.Movement.Length / Math.Pow(osuLastObj0.StrainTime, 2)[m
                                             + linearDifficulty * osuCurrObj.Movement.Length / osuCurrObj.StrainTime;[m
 [m
[31m-            double flowSnapDifficulty = 100 * areaDifficulty * osuCurrObj.Movement.Length / Math.Pow(osuCurrObj.StrainTime, 2)[m
[32m+[m[32m            double flowSnapDifficulty = 125 * areaDifficulty * osuCurrObj.Movement.Length / Math.Pow(osuCurrObj.StrainTime, 2)[m
                                             + linearDifficulty * osuLastObj0.Movement.Length / osuLastObj0.StrainTime;[m
 [m
             aimStrain = Math.Min(snapFlowDifficulty, flowSnapDifficulty);// * Math.Min(osuCurrObj.StrainTime, osuLastObj0.StrainTime) / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime);[m
[1mdiff --git a/osu.Game.Rulesets.Osu/Difficulty/Skills/Aim.cs b/osu.Game.Rulesets.Osu/Difficulty/Skills/Aim.cs[m
[1mindex aeb34f4466..e867b4bec7 100644[m
[1m--- a/osu.Game.Rulesets.Osu/Difficulty/Skills/Aim.cs[m
[1m+++ b/osu.Game.Rulesets.Osu/Difficulty/Skills/Aim.cs[m
[36m@@ -23,7 +23,7 @@[m [mpublic Aim(Mod[] mods, bool withSliders)[m
 [m
         private double currentStrain;[m
 [m
[31m-        private double skillMultiplier => 45.0;[m
[32m+[m[32m        private double skillMultiplier => 37.5;[m
         // private double skillMultiplier => 23.55;[m
         private double strainDecayBase => 0.15;[m
 [m
[1mdiff --git a/osu.Game.Rulesets.Osu/Difficulty/Skills/Speed.cs b/osu.Game.Rulesets.Osu/Difficulty/Skills/Speed.cs[m
[1mindex cf63b4ae8c..0bbe4b0212 100644[m
[1m--- a/osu.Game.Rulesets.Osu/Difficulty/Skills/Speed.cs[m
[1m+++ b/osu.Game.Rulesets.Osu/Difficulty/Skills/Speed.cs[m
[36m@@ -16,7 +16,7 @@[m [mnamespace osu.Game.Rulesets.Osu.Difficulty.Skills[m
     /// </summary>[m
     public class Speed : OsuStrainSkill[m
     {[m
[31m-        private double skillMultiplier => 1250;[m
[32m+[m[32m        private double skillMultiplier => 1275;[m
         private double strainDecayBase => 0.3;[m
 [m
         private double currentStrain;[m
