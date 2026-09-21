# Vision

The yardstick for what a-team builds next, written Working Backwards: the press release we want
to publish in September 2027, and the questions it has to answer. If a proposal doesn't move us
towards it, it doesn't ship, however good it is on its own.

## The tenet

**a-team scales the impact of the stakeholder's time and attention.** Every feature either takes
work off the stakeholder or makes the work only they can do (setting direction, approving,
judging the result) faster. If we stop doing that, a-team stops being worth using.

## Press release

### a-team gives anyone with an idea their own AI product team

**Describe the idea. A team of AI agents shapes it, builds it and ships it, while you focus on
what to build next.**

**20 September 2027.** a-team is now available to anyone with an idea and no team to build it.
Sign in with a GitHub account and a Claude account, describe what you want to exist, and a-team
puts together a product team of AI agents to make it real. You stay in charge of the direction.
The team does the rest, including while you're away.

Most ideas never get built. Hiring developers is expensive, and building it yourself is slow
even for people who can code. Turning a rough idea into precise specifications, then guiding
each change through development and release, uses up so much attention that most people can
pursue one idea at a time, if any. Ideas that deserved a quick test wait for years, and ideas
that should have been dropped take months to prove it.

a-team starts by helping you get clear on the idea: who it's for, what problem it solves, and
what a great result looks like. From there a product lead researches opportunities, asks you
questions, and pitches solutions with mockups for you to approve. Approved pitches are broken
into small increments, each one useful on its own, and developers build them. You never see
code, pull requests or servers. You get a link to try, and you either accept the change or say
what's wrong. Work starts from events, not from you: your feedback, an approved pitch, a
finished task. So the team keeps moving when you step away.

Whether you run one team or dozens, the a-team dashboard shows every one of them: which agents
are busy, which are idle, and exactly what needs your decision. You can act on any of it right
there. Start a small team to test a new idea, add agents as an idea gains momentum, or bring in
a strategy lead to rethink the vision with you as a project grows. Pull capacity back from lower
priorities, or end an experiment that isn't working.

"I want it to be easy to turn an idea into something real and find out whether it works," said
James Crosswell, creator of a-team. "When an idea is cheap to build, it's cheap to walk away
from, so you can afford to try many more of them."

Getting started takes a few minutes. Sign in with GitHub, connect your Claude account, and
describe your first idea.

"I'd had an idea for years for a tool my team could use to manage our work, but we never had
the budget for developers," said a property manager who started an a-team last spring. "a-team
helped me get crisp about what I actually wanted, then got straight to work. I felt like the CTO
of a rocket-ship start-up. Within a day I had something that would have taken a real team
months. Within a week it was everything I'd imagined. After a month it was doing things I'd
never dreamed of."

## Customer FAQ

**Who is it for?**
Anyone with ideas and no team to build them. The first user is a solo developer running several
product ideas at once. The customer we're building towards may never have written code.

**How do I use it?**
Through the a-team dashboard, and in time a web portal. The CLI installs a-team and is what the
agents themselves run; you shouldn't need it.

**Do I need to know how to code?**
No. You judge the product by using it, not by reading its code.

**Can I bring an existing project?**
Yes. The first team was put to work on an existing product, TuiCode.

**Where does the thing it builds live, and who owns it?**
The code lives in your own GitHub repo, so everything the team builds is yours. How it ships
depends on what it is, and working that out is part of the team's job: TuiCode ships as a
Homebrew package, with Linux packages next; a web app would be deployed and hosted.

**What stops the team doing something I didn't want?**
Two gates that only you hold. Nothing gets built until you approve the pitch, and nothing gets
released until you accept it. Ideas the team finds on its own aren't pitched until you give
them a priority. Everything between the gates, the team does without you.

**How much of my time does it take?**
As much as you want to give it. An hour a day makes solid progress on an idea, because your
attention is only spent where it's needed. Give it more and it does more.

**What is it bad at?**
Anything that needs humans to build or test it. Physical products, research with real people,
and sign-off from third parties all move at human speed, and a-team can't change that.

**What does it cost?**
You bring your own Claude account. We haven't decided how a-team itself will be paid for.

## Internal FAQ

**What's the most likely reason this fails?**
We stop automating the stakeholder's work. Every release has to leave the stakeholder with less
to do, or make what's left quicker. The gates stay, but each one should take seconds: a clear
question, a link to try, one click to answer.

**How will we know it's working?**
Accepted changes per stakeholder hour. Accepted means it passed the release gate: a merged PR or
an accepted pitch. Stakeholder hours are the time spent at gates and giving feedback, which
a-team can log because every approval, comment and merge goes through it. Commits per day is a
cheap sanity check alongside it, not the measure.

**What's the evidence so far?**
One project for one day: the first version of a-team, run on TuiCode, delivered more in a day
than six months of herding PRs through Claude by hand. That's promising, but the release
promises tens of projects, so we still have to show it holds at that scale.

**Can one person really run fifteen teams?**
Only if the gates are cheap. Fifteen teams are fifteen streams of pitches and releases waiting
on one person, so the dashboard has to put what needs the stakeholder in front of them, in
order, and let them answer without leaving it.

**How does a non-developer grant what deployment needs?**
Open. Shipping can need things only the customer can provide: a cloud account, a domain, a
payment method, an app-store listing. The team needs a way to ask for them, and the customer a
safe way to give them, without either one handling credentials in the open.

**Is a-team a business or an open-source tool?**
Undecided on purpose. First deliver value, then work out how to charge for it. Meanwhile, avoid
choices that close the door on a hosted version: don't let "it runs on your machine" spread
further than it has to.

**How far is today from the press release?**
Today a-team is a Lead and a Dev on one repo each, run by a dispatcher on the stakeholder's Mac,
reviewed through GitHub. Still to build:

- Acting on every gate from the dashboard, without going to GitHub
- Onboarding that captures a new idea Working Backwards, like this document
- A non-developer experience: a link to try instead of a PR, and a web portal alongside the CLI
- Portfolio controls: start, scale, pause and cancel teams; add agents and roles
- A strategy lead for periodic vision reviews
- Deployment chosen and carried out by the team
- Logging stakeholder time and accepted changes

## Where features land

The tenet says what every feature has to do. This says where it goes.

**Everything the stakeholder does lives in one place, reached from a command palette.** Today
that place is the dashboard. Anything that needs the stakeholder — answering a gate, starting a
team, pausing one, changing a setting, seeing what a team is waiting on — is a command in it.
Nothing that needs them is somewhere else.

**The CLI has three jobs, and stakeholder work isn't one of them:** getting a-team onto a
machine (`install`), the agents' own API (`board`), and debugging (`run`, `task-prompt`). It
isn't going away, and a command may well have both a dashboard and a CLI form; what it can't
have is only the CLI one.

**A capability is a command before it's a key.** Every action is registered with an id and a
label, so it shows up in the palette, can be rebound, and can be driven from somewhere else
later. That is what makes the web portal a second front-end rather than a second product, so
building it this way costs nothing now and saves the portal.

Two things this deliberately doesn't say. It doesn't say every interaction has to be a dialog:
a conversation is better as a session in the terminal than as a wizard, and the rule is
satisfied when the palette is what starts it. And it doesn't make the dashboard the only
front-end for ever — the portal is coming — only the one home there is at a time.

## Next themes

Roughly in order. Each is a direction, not a commitment; pitches turn them into work.

1. **Run the teams from the dashboard.** It's where the stakeholder already spends their day.
   Fit more than one team (#12), browse earlier sessions (#2), logs you can scan at a glance
   (#9); then a command palette and a registry to hang commands off (#86, #49), which is what
   every action after it is reached by; and then act on every gate from it: approve a pitch,
   give feedback, accept work.
2. **Trust the loop.** Feedback is never lost and the team never spins, so the stakeholder
   never has to repeat themselves or chase a stuck item: comments mid-run or older than a day
   (#3, #5), stuck triggers (#4), readyLow counting blocked tasks (#23), shelving a pitch on
   request (#24, #26).
3. **Guardrails that don't depend on the model.** A spending cap and API headroom (#13), and
   the team's own GitHub identity so GitHub enforces the gates (#6).
4. **Beyond the first user.** Starting a team without knowing how a-team works: setup that
   checks itself, a dispatcher beyond macOS launchd (#6), shared release tooling (#25), and
   first steps towards the non-developer experience.

## How to judge a proposal

- Does it save the stakeholder attention, or spend it?
- Does it keep both gates in the stakeholder's hands, enforced by a script rather than a
  sentence?
- Does it move a-team towards the press release, or only polish today's version?
- Does it land where the stakeholder already is, or does it add somewhere else to go?
- Does it keep an idle team free and a busy one bounded?
- Is it the smallest version that's actually useful?
