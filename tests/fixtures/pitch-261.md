## Opportunity

**Looking at the Pitches column, I can't tell which pitches only need my yes and which have the Lead waiting on an answer.** So I open each one to find out, or I leave them all alone.

You hold back approving finished pitches on purpose: the Dev is busy and you want it to finish what it's started. That's a fine reason to leave a pitch sitting. It's not a fine reason to leave a question sitting. But from the Work area the two look the same: a `✓ #259 …` card under **Pitches · 5**.

There's a second half to this. **As pitches are written today, the Lead isn't actually blocked on any of them.** Every open question has a recommendation, and `roles/lead.md` step 2.1 has the Lead take its own recommendation for anything you approve without answering. So approving already answers every question. A pitch that genuinely can't go ahead without you looks exactly the same as one that can, and nothing on the card tells you.

**Evidence**

- Your idea below.
- Every other pitch in Pitched right now has open questions (#219: 3, #232: 3, #257: 4, #259: 2), and every one of them comes with *I'd …*. Whether a pitch "has open questions" doesn't separate anything.
- `roles/lead.md` has one **Open questions** heading, described as "what you'd like the reviewer to decide". That mixes up "tell me if I've got this wrong" with "I can't choose without you".
- `dashboard/WorkView.cs` `Gates`: **Questions** holds only `Status == "Ready"`, i.e. the Dev's blocked tasks (#226, shipped in #255). `ReaderDialog` always titles a question `· the Dev's question`. Pitches go to **Pitches** or **Triage** by priority alone.
- `dashboard/Card.cs`: a card's only icon is whose move it is (`YourMove` / `TheirMove`). In **Review** a finished pitch and a task's PR look the same, and so would a pitch and a task in **Questions** once both live there. The PR row is written out as `PR #n`.
- #169 (deferred until there's a hosted app) is the bigger version of this: questions asked and answered in a structured way, and a decision register. This pitch shouldn't get in its way.

## Options considered

1. **Do nothing.** Open each pitch to see what it asks. That costs attention on every pitch, every time you look, and the cost grows with the number of teams.
2. **Put every pitch with open questions in Questions** (your suggestion, taken literally). Cheap: the board already reads the body. But today that's every pitch, so Pitches would empty out and Questions would hold the same five cards. Nothing would be separated.
3. **The Lead splits what it assumes from what it asks, and only a pitch that asks goes to Questions** (proposed). Questions the Lead can call itself become **Assumed**, and approving takes them as they stand. Only a real fork goes under **Needs your answer**, and a pitch with one shows in Questions with a pitch icon. This puts the separation where you judge it, and it follows *stop the team asking what it could have decided itself* (vision theme 2). The cost is a change to the Lead's pitch format as well as the dashboard.
4. **Approve whenever, build when there's room.** Approved pitches queue, and the Lead only breaks one down once Building has room. That removes the reason you hold approvals back. But it changes how work flows, not what you can see, and a pitch that asks a real question still looks like one that doesn't. It's worth its own Idea.
5. **Structured questions in the dashboard** (#169's direction): each question gets its own control and you answer in place. That's the end state, but it's deferred until there's a hosted app and a database, and it's far bigger than this.

## Proposal

**A pitch only shows under Questions when the Lead genuinely needs your answer, and every card shows what kind of thing it is.**

- **Pitch format**: **Open questions** is split in two.
  - **Assumed**: what the Lead has decided, each with its reasoning in a line. Approving accepts these. Comment to change one.
  - **Needs your answer**: only the questions the Lead can't settle itself, such as a direction change, a trade-off with no default, or something only you know. Usually empty, in which case the heading is left out.
- **Questions column**: along with the Dev's blocked tasks, it holds any Pitched pitch that has a **Needs your answer** section. The reader shows that section as the question, the same way it shows the Dev's today (`· the Lead's question`). When you reply, the card turns to the Lead (`lead · answering your feedback`). The Lead folds your answer into **Assumed** and removes the section, and the pitch goes back to **Pitches** to wait for your yes.
- **Pitches column**: now only holds pitches that need nothing but approval, so it's safe to leave until the Dev has room.
- **Kind icons**: each card gets a second icon after the turn icon. It's `nf-md-presentation` for a pitch and `nf-cod-code_review` for a task. In Unicode it's `◇` and `‹›`. This applies in every column, since Review and Questions both mix the two.
- **PR row**: with Nerd Font icons, `nf-dev-git_pull_request` replaces the word `PR`. In Unicode it stays `PR #n`.
- **The Lead's own runs**: when it revises a pitch after your feedback, it moves answered questions into **Assumed**. It also rewrites pitches still in Exploring into the new format before promoting them. Pitches already in Pitched are left as they are, so they show in **Pitches** until you comment.

## Mockup

A lane in Work, Nerd Font icons (glyphs drawn as `▭` pitch, `⟨⟩` task, `⎇` PR so they read here). Since #255 the column you're in takes half the lane; this shows Questions selected:

```
a-team ──────────────────────────────────────────────────────────────────────────────────────────────
╭ Pitches · 4 ────────────╮╭ Questions · 2 ─────────────────────────────────╮╭ Review · 2 ─────────────╮
│✓ ▭ #259  The Work area …││✓ ▭ #257  I wait on the team while its Dev bui…││· ⟨⟩ #262  dev · CI run…│
│✓ ▭ #219  I can reply to…││✓ ⟨⟩ #254  Answering the Dev's question is eno…││   └ ⎇ #270  The menu b…│
│✓ ▭ #232  A follow-up fi…││                                                ││✓ ▭ #142  There's nowhe…│
│✓ ▭ #261  I can't tell w…││                                                ││                        │
╰─────────────────────────╯╰────────────────────────────────────────────────╯╰────────────────────────╯
```

The same lane with Unicode icons: `✓ ◇ #259 …`, `✓ ‹› #254 …`, and `└ PR #270 …`.

Enter on #257 in Questions, the existing reader (`Dialog` with a scrolling `Markdown` view):

```
┌┤#257  I wait on the team while its Dev builds one task at a time · the Lead's question├┐
│ ## Needs your answer                                                                  │
│                                                                                       │
│ 1. **Should Devs replace Worktrees?** Worktrees caps PRs waiting on you plus builds;  │
│    Devs caps builds at once. I can't pick without knowing which of those you want     │
│    to control…                                                                        │
│                                                                                       │
│  Up/Down/PgUp/PgDn scroll   a approve   g on GitHub   Esc close                        │
└───────────────────────────────────────────────────────────────────────────────────────┘
```

A pitch in the new format, as the reader shows it below its Proposal:

```
## Assumed

Approving accepts these; comment to change one.

1. **Squash-merge always.** Every merge on `main` has been one.
2. **Accept on the card as well as in the reader**, behind the same confirmation.

## Needs your answer

1. **Should Devs replace Worktrees?** …
```

## Scope

**In**

- `roles/lead.md`: **Assumed** and **Needs your answer** replace **Open questions** in *Writing a pitch*. Step 1 has the Lead fold your answers into Assumed. Step 2.1 reads from Assumed. Step 4 converts a draft to the new format when promoting it.
- The board's `waiting`: a Pitched pitch whose body has a non-empty **Needs your answer** section comes with that section as its question.
- Work: **Questions** holds those pitches as well as the Dev's blocked tasks. The reader titles the question as the Lead's or the Dev's.
- Kind icons on every card, in both icon styles. The PR icon replaces `PR` under Nerd Font.
- Tests: `waiting` gives a question for a pitch with the section and none for a pitch with only Assumed or an empty section; Questions and Pitches each hold the right pitch; each card's kind icon in both styles; the PR row in both styles; the reader's title for each kind.

**Out**

- Answering a question with a control of its own, or a decision register (#169).
- Holding approved pitches until there's room (option 4). Worth its own Idea if you want it.
- Rewriting pitches already in Pitched into the new format.
- Kind icons anywhere outside Work (Agents, message bar).

## Rough breakdown

1. **The Lead only asks what it can't decide, and a pitch that asks shows under Questions.** The role change, `waiting`, the Questions column and the reader title ship together. On its own, the role change would put a heading in the body that nothing on screen responds to.
2. **Every card shows whether it's a pitch or a task, and the PR row has its own icon.** This is independent of slice 1 and small. It could land first.

## Decided

You approved without commenting, so each of these is my recommendation.

1. **A pitch with a real question leaves Pitches rather than showing in both** (mine). In both it would be counted twice.
2. **Unicode kind icons are `◇` and `‹›`** (mine). Both fit the 2-cell field and neither is already used.
3. **Only pitches in Pitched can land in Questions** (mine). A pitch in Building with a question reaches you as a comment on one of its tasks, as now.
4. **Existing Pitched pitches aren't rewritten** (mine). They stay in **Pitches**; comment on one if you want it to count as asking.
5. **A pitch with a question lands in Questions whatever its priority**, even unranked (mine, settled in breakdown). Triage is for deciding whether you want it; a question is more urgent than that.
6. **Approving a pitch that still has an unanswered question is allowed** (mine, settled in breakdown). The gate is yours. The Lead then asks it in a comment and leaves the pitch in Approved until you answer, since there's no recommendation to fall back on.

---

## Original idea

> Some of the pitches are finalised and just waiting for approval. Often times I don't approve those immediately because the devs are already busy and I don't want them working on 10 things at once (I want them to finish what they're working on before starting something new). 
>
> Some of the other pitches have open questions - they product lead is effectively blocked waiting on me. 
>
> We already have a questions area - currently this only shows cards that the dev has asked questions about and is blocked waiting for answers from me on. We could potentially also show pitches with questions there too.. and have some kind of icon that could be used to differentiate pitches from stuff that the dev is working on (maybe `nf-md-presentation` and `nf-cod-code_review`. 
>
> While we're adding icons, currently PRs in the work area are prefixed with the text `PR` - we could instead show the `nf-dev-git_pull_request` icon when nerdfonts are enabled. 

<!-- a-team:lead -->



