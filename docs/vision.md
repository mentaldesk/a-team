# Vision

The yardstick for what a-team builds next, written Working Backwards: the press release we want
to publish in September 2027, and the questions it has to answer. If a proposal doesn't move us
towards it, it doesn't ship, however good it is on its own.

## The tenet

**Attention is the scarce resource, and a-team spends the stakeholder's only on what shapes the
product.** The stakeholder decides where a product is going: its vision, which pitches are worth
chasing, and whether what was built does what they had in mind. Everything else — the code, CI,
releases, deployment, documentation, and the small design decisions in between — is the team's.
Every feature either takes work off the stakeholder or makes what only they can do faster.

**When more needs the stakeholder than they can absorb, fix their end, not the team's.** Make the
decisions fewer, faster or unnecessary. Never throttle the team so its output fits the
stakeholder's day. The stakeholder can always pull capacity back themselves: pause a team, end an
experiment. That is them setting direction, and a-team never makes that choice for them.

## Press release

### a-team gives anyone with an idea their own AI product team

**Describe the idea. A team of AI agents shapes it, builds it and ships it, while you decide
what's worth building.**

**20 September 2027.** a-team is now available to every developer with more ideas than hours.
Install it, point it at a GitHub repo, and a-team puts a product team of AI agents to work on it:
a Lead that researches and pitches, developers that build, test and ship. You set the direction
and judge the results. The team does everything in between, including while you're away.

Developers already get a lot out of Claude. But driving it by hand, in Claude Desktop or a
terminal, means herding every change yourself: deciding each detail, nudging each PR through CI,
fixing each release. Your attention goes on the minutiae, so you can push one idea at a time, and
the ideas that deserved a bold step get a series of small ones.

a-team turns that around. The Lead doesn't just polish what's there: it studies the product and
its users and pitches what you hadn't thought of, or had thought was too ambitious for now, with
a mockup that already looks like the rest of the product. Approve it, and the team breaks it into
work, builds it, checks it works the way a user would, and hands you the result to try. You don't
review code. You try the new experience and accept it, or say what's wrong — and most of the
time there's nothing to say, because the design was right first time.

The dashboard shows every team you run: which agents are busy, and exactly what needs your
decision, in the order you'd want to make it. You act on all of it right there.

"The best day with a-team is when a Lead pitches something I'd never have asked for, I approve
it, and the team nails it," said James Crosswell, creator of a-team. "I get a step change in the
product without giving fifteen bits of feedback along the way. A really great day is when that
happens five or ten times, across different products, from separate teams."

Getting started takes a few minutes: `brew install mentaldesk/tap/a-team`, connect a repo and
your Claude account, and write the vision for your first team in a short interview.

## Customer FAQ

**Who is it for?**
Developers like the creator: comfortable with GitHub and Claude, with more product ideas than
time, who today drive Claude by hand in Claude Desktop or a terminal. In time, people who have
never written code, through a web interface; that's a later step, not this release.

**What do I actually do?**
Set the vision, decide which pitches are worth chasing, and try what the team delivers. The gate
on a finished task is user testing, not code review: does it do what you thought it would when
you approved the pitch?

**How much of my time does it take?**
As much as you want to give it. The aim is that the time goes on decisions that change where a
product is heading, and almost none on correcting details.

**What kinds of product can it build?**
Today, terminal (TUI) apps, which is what it was built on. Web apps are next: building them, and
trying them locally without deploying anywhere first. Deploying to production and mobile apps
come later.

**Can I bring an existing project?**
Yes. The first teams work on existing products: TuiCode and a-team itself.

**Who owns what it builds?**
You. The code lives in your own GitHub repo.

**What stops the team doing something I didn't want?**
Two gates that only you hold, enforced by a script rather than a promise: nothing gets built
until you approve the pitch, and nothing is done until you've tried it and accepted it.

**Can my colleagues and I share a team?**
Not yet. Teams with several stakeholders are on the way, and will need a hosted service to
coordinate everyone's agents.

**What does it cost?**
You bring your own Claude subscription; running a-team for one person is free. Teams with
several stakeholders will likely need a paid, hosted coordination service.

## Internal FAQ

**What's the most likely reason this fails?**
We stop taking work off the stakeholder. Today a-team still leaves its stakeholder making lots of
small decisions, mostly because the Lead and Dev can't reliably make good UX decisions on their
own. Every small correction is attention spent on detail instead of direction.

**What's the second most likely?**
The teams stay feature factories. They're good at iterative improvement and rarely propose bold
new sections of a product that solve big problems it doesn't solve yet. Tools like Paperclip
already run for much longer without input and delegate more decisions. If a-team only ever makes
products slightly better, it isn't worth running.

**How will we know it's working?**
The real measure is how quickly the products get better, and there's no easy way to count that.
Accepted changes per stakeholder hour was the old proxy, but a change is ambiguous: one large
task that matters is worth more than several the stakeholder only vaguely cares about. Until
there's something better, the signal is the great day: a surprising pitch, approved, built right
first time, with little or no feedback in between — and how many of those happen per week across
teams. Stakeholder time per team (#426) is the cost side.

**Isn't "bigger and bolder" in tension with "less of my attention"?**
Yes. A bigger pitch that misses wastes more than a small one, and longer runs without input mean
mistakes surface later. Bolder pitches only pay off if the UX and self-verification work makes
the team right first time more often.

**Can agents check their own work?**
Not well enough yet, and it matters. For TUIs they struggle: there are many terminals, and no
easy way yet to capture what the screen looks like during a test. If agents can't see what they
built, the stakeholder becomes the tester of last resort for things a machine should have caught.

**Is a-team too specialised?**
It could become one. TUI apps are niche, and a-team grew up building them. Broader appeal needs
web apps: building them, testing them locally without deploying via Vercel, then deploying to
production infrastructure. Mobile is much later; certificates and store signing make it a
nightmare today.

**The headline says "anyone with an idea", but the customer is developers. Which is it?**
Both, on different horizons. The headline is where it goes; the release in September 2027 is for
developers. The non-coder needs a web interface and deployment handled for them, which are Later.
A pitch that serves the non-coder at the developer's expense is early.

**Is a-team a business or an open-source tool?**
One idea: the single-user experience stays free, and a hosted coordinator and project service is
paid. It coordinates agents for teams with several stakeholders, and in time replaces GitHub
Projects for workflow management. Avoid choices that close that door: don't let "it all runs on
one machine" spread further than it has to.

**Isn't the platforms building this in a threat?**
GitHub Agent HQ, Claude Code agent teams and Paperclip are all heading towards orchestration. We
build on them rather than compete head-on; what a-team owns is the product loop — a Lead that
pitches, gates the stakeholder holds, and spending their attention only on direction.

**Does cost limit it?**
Not today. Current use comes nowhere near the limits of a Claude subscription.

## Where features land

**Everything the stakeholder does lives in one place, reached from a command palette.** Today
that place is the dashboard. Answering a gate, starting a team, pausing one, changing a setting,
seeing what a team is waiting on: each is a command in it.

**The CLI has three jobs, and stakeholder work isn't one of them:** getting a-team onto a
machine (`install`), the agents' own API (`board`), and debugging (`run`, `task-prompt`). A
command may have both a dashboard and a CLI form, but never only the CLI one, and the CLI form
never ships ahead of the dashboard command.

**A capability is a command before it's a key.** Every action is registered with an id and a
label, so it shows up in the palette, can be rebound, and can later be driven from a web
front-end without becoming a second product.

## Who we learn from

None of these has been studied in depth yet; that's the Lead's homework (#76). Look for what to
borrow and what to avoid, and for newer ones.

- **Claude Desktop, Claude Code by hand**: what the customer uses today. a-team has to be clearly
  less work than driving Claude directly.
- **[Paperclip](https://github.com/paperclipai/paperclip)**: agents in an org chart with budgets,
  running for long stretches with decisions delegated. The ambition and autonomy our teams lack.
- **[MentalDesk TUI style guide](https://github.com/mentaldesk/tui-style-guide)**: our own source
  of consistent UX. Improve it whenever a team's design needed correcting.
- **[Vibe Kanban](https://vibekanban.com/), [Nimbalyst](https://nimbalyst.com/)**: kanban boards
  over coding agents; the closest to our board-driven loop.
- **[OpenAI Symphony](https://github.com/openai/symphony)**: an issue tracker as the control plane
  for agents. Our Dev loop, without a Lead.
- **[Orca](https://github.com/stablyai/orca), [Conductor](https://www.conductor.build/),
  [Superset](https://superset.sh/)**: one developer supervising many agents. The attention cost
  we remove.
- **[Atoms](https://atoms.dev/), [Replit Agent](https://replit.com/),
  [Lovable](https://lovable.dev/)**: idea to app for people who don't code. Our Later customer.
- **[GitHub Agent HQ](https://github.blog/news-insights/company-news/welcome-home-agents/),
  Claude Code agent teams**: platforms building orchestration in. Build on them, don't compete.

## Next themes

In order. Each is a direction, not a commitment; pitches turn them into work.

### Now: super slick for developers like the creator

1. **Consistently good UX.** Designs that are right first time and consistent with the rest of the
   product, using and improving the tui-style-guide, so the stakeholder stops correcting details.
2. **Agents verify their own work.** See what they built the way a user would, including
   capturing what a TUI looks like across terminals, so the stakeholder isn't the first to find
   what's broken.
3. **Think bigger.** Bold pitches for whole new parts of a product that solve problems it doesn't
   solve yet, and longer stretches of work without needing the stakeholder.
4. **Fewer small decisions.** The task gate is user testing, not code review; everything that
   isn't direction or judging the result moves off the stakeholder.

### Next

5. **Beyond TUIs.** Build web apps, and try them locally without deploying via Vercel first.
6. **Multiple stakeholders.** Teams shared by several people, coordinated by a hosted coordinator
   and project service: free for one user, paid for teams, eventually replacing GitHub Projects.

### Later

7. **Deploy to production.** Web apps shipped to real infrastructure by the team.
8. **Non-coders.** A web interface, for people who have never written code.
9. **Mobile apps.**

## How to judge a proposal

- Does it spend the stakeholder's attention on direction, or on detail?
- Will the stakeholder accept the result without a round of corrections?
- Is it a step change for the product, or one more small iteration?
- Does it keep both gates in the stakeholder's hands, enforced by a script rather than a
  sentence?
- Does it raise what the stakeholder can get through, or lower what the team delivers to fit?
- Is it Now, or is it reaching for Next or Later early?
- Does it land where the stakeholder already is, rather than adding somewhere else to go?
- Is it the smallest version that's actually useful — without being a small idea?
