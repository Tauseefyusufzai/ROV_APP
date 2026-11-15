# Scoring Explanation (Updated)

This document explains the updated score calculation, the accuracy bonus, penalties, the percent display, and full-positive max score logic. All formulas match the current `EventLogDetail` logic.

## 1. Inputs and weights

Weights (Inspector defaults):
- killWeight = 2
- injuredWeight = 1
- grenadeStunnedWeight = 0.5
- iedDefuseSuccessWeight = 3
- iedDefuseFailWeight = -2
- friendlyFireWeight = -4
- reviveWeight = -2
- statusKilledPenalty = -2
- accuracyWeight = 0.3
- maxPenalty = -10

Counts (per player in a recording):
- killedCount, injuredCount (from person status casualties)
- grenadeKillCount, grenadeInjuredCount, grenadeStunnedCount (from grenade status)
- iedDefuseSuccessCount, iedDefuseFailCount, iedKillCount, iedInjuredCount (from event log)
- friendlyFireCount, grenadeFriendlyFireCount (from person + grenade status)
- reviveCount (from event log)
- deviceStatus (0=Fit, 1=Injured, 2=Killed)
- hits, misses (from weapon hit/miss counts)

## 2. Raw accuracy

Accuracy is a ratio in the range 0..1:

$$
\text{accuracy} = \frac{\text{hits}}{\text{hits} + \text{misses}}
$$

If total shots is 0, accuracy is 0.

## 3. Score formula

Totals used by the score:

$$
\text{totalKills} = \text{killedCount} + \text{grenadeKillCount} + \text{iedKillCount}
$$

$$
\text{totalInjured} = \text{injuredCount} + \text{grenadeInjuredCount} + \text{iedInjuredCount}
$$

Positive score:

$$
\text{positive} = (2 \cdot \text{totalKills}) + (1 \cdot \text{totalInjured}) + (0.5 \cdot \text{grenadeStunnedCount}) + (3 \cdot \text{iedDefuseSuccessCount})
$$

Accuracy bonus:

$$
\text{accuracyBonus} = \text{positive} \cdot \text{accuracy} \cdot 0.3
$$

Penalties (then clamped by maxPenalty):

$$
\text{penalties} = (-2 \cdot \text{iedDefuseFailCount}) + (-4 \cdot (\text{friendlyFireCount} + \text{grenadeFriendlyFireCount})) + (-2 \cdot \text{reviveCount}) + (\text{deviceStatus} == 2 ? -2 : 0)
$$

$$
\text{penalties} = \max(-10, \text{penalties})
$$

Final score:

$$
\text{score} = \text{positive} + \text{accuracyBonus} + \text{penalties}
$$

Note: `iedNotAttemptedWeight` is currently not used in the score calculation.

## 4. Full-positive max score

The score percent is normalized by a "full-positive" cap based on the exercise size:

Let:
- participants = number of devices in the exercise
- opponents = max(0, participants - 1)
- iedDevices = number of IEDs in the exercise

Then:

$$
\text{maxPositive} = (2 \cdot \text{opponents}) + (1 \cdot \text{opponents}) + (0.5 \cdot \text{opponents}) + (3 \cdot \text{iedDevices})
$$

$$
\text{maxScoreFull} = \text{maxPositive} \cdot (1 + 0.3)
$$

Percent displayed:

$$
\text{percent} = \mathrm{clamp}\left(\frac{\text{score}}{\text{maxScoreFull}} \cdot 100, 0, 100\right)
$$

If `maxScoreFull` is 0, the UI falls back to the fixed `maxScore` setting.

## 5. Worked example (device 160, latest exercise)

Counts (from the DB):
- totalKills = 5
- totalInjured = 1
- grenadeStunnedCount = 0
- iedDefuseSuccessCount = 0
- hits = 6, misses = 52 => accuracy = 6 / 58 = 0.103
- friendlyFireCount = 3
- reviveCount = 0
- deviceStatus = Fit
- participants = 10 => opponents = 9
- iedDevices = 0

Step-by-step:

1) Positive score

$$
\text{positive} = (2 \cdot 5) + (1 \cdot 1) + (0.5 \cdot 0) + (3 \cdot 0) = 11
$$

2) Accuracy bonus

$$
\text{accuracyBonus} = 11 \cdot 0.103 \cdot 0.3 = 0.341
$$

3) Penalties

$$
\text{penalties} = -4 \cdot 3 = -12 \;\;\Rightarrow\;\; \max(-10, -12) = -10
$$

4) Final score

$$
\text{score} = 11 + 0.341 - 10 = 1.341
$$

5) Full-positive max score

$$
\text{maxPositive} = (2+1+0.5) \cdot 9 + (3 \cdot 0) = 31.5
$$

$$
\text{maxScoreFull} = 31.5 \cdot 1.3 = 40.95
$$

6) Percent

$$
\text{percent} = \frac{1.341}{40.95} \cdot 100 = 3.28\%\; (\text{UI rounds to } 3.3\%)
$$

If your UI is showing ~9.9%, that means the UI is using a smaller participant count (for example, 4 participants -> opponents = 3), which reduces the maxScoreFull and raises the percent. Ensure the UI has the correct participant count loaded when calculating `maxScoreFull`.

## 6. Key takeaways

- Accuracy only boosts the positive score. If positives are small or penalties are large, percent stays low even with decent accuracy.
- Full-positive normalization depends on participants and IED count. If those are missing or partial, percent will be inflated.
- Friendly fire penalties are large and clamped at -10, which can dominate the score.
