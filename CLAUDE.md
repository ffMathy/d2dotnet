# Working on d2dotnet

## Testing the SampleBot live: prove it with screenshots

When the SampleBot is tested against the running game (a boss such as Blood Raven, a quest, town chores, any claim
of progress), prove it with pictures, not only log lines:

- Run the bot with `--trail`. It saves a screenshot every 3 seconds and one at each key moment (arrival in an area,
  quest start and done, a boss or summoner found, fought and killed, a death, talking to a quest giver) in its own
  folder under `%LOCALAPPDATA%\D2Dotnet\screenshots\trail\<run>`. Trails older than two days are deleted.
- Look at the pictures yourself, and check that they show what the log claims.
- Show the relevant screenshots to the user **along the way**, as the run goes (start, mid-fight, the kill, the quest
  done, anything that went wrong), not only at the end. Send a few telling ones, not all of them.

## Commands

- The .NET 10 SDK is at `~/.dotnet`: `export DOTNET_ROOT=~/.dotnet && ~/.dotnet/dotnet ...`.
- Wrap every command in `timeout`. The full test suite takes about 23 minutes (the `D2Dotnet.Tests.Maps` oracles are
  slow); filter tests to what changed, for example `--filter "FullyQualifiedName!~D2Dotnet.Tests.Maps"`.
- While the bot runs, its `bin` folder is locked: build to another folder with `-o` to check that it compiles.
