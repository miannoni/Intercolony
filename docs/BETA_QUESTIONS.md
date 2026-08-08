# Beta feedback questions

This is a question bank for the 0.9.0 beta, not a script that must be used whole. The beta needs
evidence about balance, exploits, compatibility, UX, performance and unexpected faction or pawn
interactions. It does not need a general satisfaction survey.

The useful unit is usually an example: what the player tried, what they expected, what happened and
what choice it changed. A rating without an example is hard to turn into a code, UI or balance
change.

## Critique of the current draft

### "Found any bugs?"

Keep a bug prompt, but not this wording. It will mostly produce "yes", "no" or a result without the
steps needed to reproduce it. It also misses failures that players do not label as bugs, such as a
record vanishing after load or a report becoming very slow.

Better: **Did anything break, vanish, duplicate or behave differently after saving and loading? What
were you doing, what did you expect and what happened? Please include the mod and DLC list and UI
scale.**

### "How did you use the mod — what was the story you played? ..."

The open story angle is worth keeping as context. It can expose a use the design did not anticipate,
and an answer in the player's own terms is richer than making them select "trader" or "supplier".
The tycoon/nomad and plunder/community examples are evocative, but they also lead the answer. They
invite the respondent to perform a story while leaving out whether the systems supported it.

Better: **What were you trying to do with Intercolony? Did its offers, purchases, contracts or hires
support that playstyle, or push you toward a different one? Give one example.** Make this a short
free-text opener, not the main balance question.

### "How did the main systems of the game react ... too OP ... too clunky?"

This is too broad and joins two different questions. "Main systems of the game" could mean RimWorld
or Intercolony. "OP" asks for a verdict without showing where the numbers went wrong; "clunky" does
the same for UI.

Better balance prompt: **Name one sale, purchase, contract or hire that felt too rewarding, too
costly or not worth doing. What choice did it change?** Ask about UI separately with a concrete task.

### "What were the main ways you used to buy and sell? ..."

This is one of the more useful drafts because it can show which loops players adopt and ignore. The
examples still lead the response, and simply naming a route does not explain whether another route
was confusing, weak or unnecessary.

Better: **Which commerce or procurement route did you use most, and which did you ignore? Why? Name
one time price, distance, deadline or delivery mode changed your choice.**

### "What would you like to see in the mod?"

Cut this from the core questionnaire. It invites feature requests during a beta whose unknowns are
whether the existing design works. A popular answer may still have no bearing on 1.0. If an open end
is wanted, constrain it to a real obstruction: **What one missing piece prevented the playstyle you
were trying?** Keep that optional.

## Candidate questions, grouped by what they learn

Every question below has a decision attached. If the project would not act on the answer, the
question does not belong in the form.

### Play context and actual use

**Q1. What were you trying to do with Intercolony, and did it support that playstyle or push you
toward another loop? Name the routes you actually used and one you ignored.**

Useful answers let us change which routes are introduced, made more visible or rebalanced; they also
show whether an unexpected playstyle is being supported accidentally or blocked needlessly.

Collect play time, colony age and whether this was a new or existing save as short fields beside Q1.
They are needed to distinguish a first-session impression from a long-game result.

### Meaningful demand, scarcity and distance

**Q2. Across several offer refreshes, name one sale or purchase you accepted and one you rejected.
What made the difference: quantity, price, scarcity, distance, deadline or delivery mode? If none of
those changed a decision, say so.**

Useful answers let us tune demand mix and quantities, RFQ response/capacity/spreads, distance effects,
lead times and fulfillment modes instead of treating valid generated rows as proof of useful choices.

**Q3. After playing for a quadrum or longer, did Intercolony make money or scarce goods too easy,
stay useful without taking over, or become irrelevant? Give one turning point and roughly how far
into the colony it happened.**

Useful answers let us change the economy's first-pass prices, quantities, refresh rates, costs and
scaling over time. This is the long-game judgement no self-test can supply.

### Balance and exploit discovery

**Q4. What is the strongest trick or repeatable strategy you found for making money, getting scarce
goods or avoiding a cost? Explain how to repeat it, especially if it felt like you had outsmarted the
system.**

Useful answers let us reproduce exploits and then add a limit, cost or counter-pressure without
making the player frame a clever discovery as a confession.

### Obligations, failures and deadlines

**Q5. For an active sale, RFQ or purchase, recurring contract or hire, could you tell what you owed,
where it had to happen and when it was due? Describe the first place you hesitated or guessed.**

Useful answers let us change row text, tooltips, confirmations, tab structure and deadline emphasis;
they also test whether obligations remain understandable across the complete flow.

**Q6. When something failed or was unavailable, did the game tell you the cause and the next action?
Include the exact message or a screenshot if possible.**

Useful answers let us repair failure text for deadline, quality, material, condition, stock, silver,
pickup and expired-RFQ cases instead of only knowing that the action did not work.

### Compatibility and unexpected pawn interactions

**Q7. Which RimWorld version, DLC, UI scale and relevant mods were active? Did you complete a sale or
purchase with a DLC/modded item, or view an active employee through a colonist bar, work-tab or roster
mod? Say what happened.**

Useful answers let us record an honest compatibility matrix, fix scale-specific layouts, fix generic
item handling and change employee integration where another mod assumes every player-faction pawn is
a permanent colonist.

Use specific fields for version, DLC and UI scale, plus free text for the mod list and observed
interaction. Do not ask "Was it compatible?"; a load order starting is weaker evidence than a named
transaction or employee interaction.

### Persistence and employee lifecycle

**Q8. If you kept an employee through saves, reloads or renewals, or saw one downed, captured or leave,
what survived and what looked wrong? Mention how long they stayed and whether faction, ideology,
relations, bed or work assignment changed.**

Useful answers let us fix long-run pawn-state drift, missing references, save corruption and the
recovery-to-departure cleanup branch that has not yet been seen complete.

Ask Q8 only when the respondent used labor. A non-user cannot add evidence here.

### Performance and dense UI

**Q9. Did the mod become noticeably slower or cause pauses? What were you doing, and roughly how old
and large was the colony or world?**

Useful answers let us choose a production case to profile and set a practical performance threshold
beyond the one tested machine and load order.

**Q10. If Business contained revenue, purchases and payroll together, did it still read as a summary?
What was the first row or figure you could not find or understand?**

Useful answers let us cut or reorganize report rows at full density rather than shrink the text or
judge the empty report.

Ask Q10 only when all three kinds of activity appear in Business.

## Recommended final set

A realistic form is **one setup block, five core response blocks and four short conditional
follow-ups**. It should take about 8–10 minutes for a player with examples. Longer specialist testing
belongs in a play-test checklist, not in the public feedback form.

1. **Setup:** RimWorld version, DLC, UI scale, relevant mods, new/existing save, approximate play time
   and colony age. Add the observation part of Q7 if the player traded a DLC/modded item.
2. **Playstyle and routes:** Q1.
3. **Meaningful choices:** Q2, followed by Q3 only for players who reached a quadrum. These may share
   one free-text box, but keep both prompts visible.
4. **Clever strategies and exploits:** Q4.
5. **Obligations and explanations:** Q5 and Q6 in one box: first confusion, then any failed action and
   its message.
6. **Breakage and performance:** the reworded bug prompt from the critique, followed by Q9 only if the
   player noticed slowdown. Ask for steps, expectation, result and a screenshot or log where possible.
7. **Labor, conditional:** Q8, plus the pawn-management observation from Q7 if relevant.
8. **Dense Business report, conditional:** Q10.

If somebody answers only three things, the most valuable are **Q2 (a real economy decision), Q4 (a
repeatable exploit or dominant strategy) and Q7's setup plus observed compatibility interaction**.
Those reach the largest gaps that local self-tests and the current single load order cannot close.

Most substantive answers should be free text because the example is the evidence. Setup, play time,
UI scale, DLC, systems used and whether a save/reload occurred should be specific fields or
checkboxes. A 1–5 balance or satisfaction rating can be an optional sorting aid, but it must never
replace the example that explains what should change.

## Questions deliberately cut

- **The broad wish list.** It steers the beta toward feature voting. Keep only the optional
  playstyle-blocker version if there is room.
- **The generic "Found any bugs?" wording.** The structured breakage prompt covers it and requests
  reproducible evidence.
- **A separate question for every one of the 11 unproven criteria.** Several share the same player
  observation, and the full matrix would turn feedback into unpaid test execution.
- **"Was it balanced/compatible/intuitive?" ratings.** These label a feeling but do not identify a
  number, interaction or screen to change.
- **An exhaustive list of every failure type, DLC combination or pawn edge case.** Use those lists to
  route and interpret answers internally; showing all of them makes the form look longer and leads
  respondents toward cases they did not encounter.
