# Changelog

Changes in this fork (aradix85/Sleeptalker), on top of Sleeptalker 0.9.0 by
EarthboundPromoter. Work branch: `fix/responsiveness`. Every change was verified in game
through a local dev bridge (injected key presses, spoken output read back with frame
numbers). The guiding rule: stay as close as possible to the game's own behaviour, driving
its designed controls and FSM events. Several fixes are shared with the same changes in
the Citizen Speaker fork.

## Unreleased

### Enter on a location could vanish
- The Location Button's onClick only sends its FSM "Clicked", and the FSM takes it in
  three states: Idle, MouseOn and Selected. In every other state the press is dropped
  without a sound: Is New? (0.5 s) and New Animation on a location that just appeared,
  Off Screen while the UI Camera cannot yet see the marker (a pan still on its way),
  cooldown, and the one-second close transitions after leaving a location. The button
  stays interactable throughout, so the commit gate passed and Enter did nothing; the
  player heard only the row again (SUBSTRUCTURE, a location that had just appeared).
- The commit gate now also asks the button's FSM whether its current state handles
  Clicked. If not, the Enter is held, like the existing camera-in-flight hold, and
  pressed the moment the FSM can take it (hold extended from 2 to 3 seconds; a timeout
  logs the FSM state). Buttons whose FSM never handles Clicked are not gated.
- Verified live, A/B on the same save: Down onto SUBSTRUCTURE then Enter 4, 8, 12 and
  16 frames later, mid-pan. With the gate: opened 4 of 4. With the gate patched out at
  runtime: opened 0 of 4.

### Twin locations
- A location can exist as two variants with the same name at the same position (contract
  Darkside: "Abandoned Unit" and "Abandoned Unit 2", the hideout, swapped by
  ANKHITAVISIT). Both were rows; Enter on the undrawn one held until "Still moving, press
  Enter again." forever. Twins now fold into one row that prefers the drawn variant, and
  Enter presses whichever variant the game draws, also while a held Enter waits.

### Waking inside a location
- A location card can end the cycle (END CYCLE in the Darkside hideout). The game then
  wakes the player INSIDE that location: the Cycle Controller stores it as Home Actions,
  hides it and the Leave Button for the cycle, and its "Actions On + Save on next leave"
  state switches both back on, but leaves Action View? false. The mod read the station
  instead, so the player heard station rows, Backspace never pressed Leave, and the
  location stayed open underneath: its Location Button in Location Active, the UI Camera
  and Station Orbital Control off, its dial in Selected. No other location could draw or
  open; the old Abandoned Unit variant stayed on top of the hideout that replaced it.
- The Leave Button global is now the mode model's sign of an open location, and the
  location table falls back to the open location's own group (named by its Location
  Button in Location Active) when the render walk misses it. After such a cycle the
  player hears the location's cards and Backspace leaves it the game's own way.

### Enter on the station opened the wrong location
- Station rows are sorted by their angle around the rig centre, and both ends of that
  angle moved with the camera: the marker is billboard-displaced and tilts toward it, and
  Focus Body rides the camera's scroll (z 192.7 to -601.1 over two rows). Close
  neighbours swapped order between rebuilds: at Solheim OLD RELAY and OLD FABRICATOR went
  from 102.3/100.7 to 97.0/97.5 after one pan, so the row the player had just heard
  pointed at the other node and Enter opened OLD FABRICATOR. Rows are now measured from
  each location's authored root around a centre frozen once per scene, so the order holds
  for the visit. Verified live: Enter on OLD RELAY opens OLD RELAY (DISENGAGE SYSTEMS).
- Ruled out on the way, live: the game does not submit its own selection on a keyboard
  Enter (row SUNCATCHERS, selection THE RIG, Enter opened SUNCATCHERS), so no input gate
  was needed.

### Conversations over the Drive Log
- A conversation that opens while the Drive Log is open (Kadet at Bliss's Bay) left the
  game's selection on the log's quest buttons: arrows walked "MAKE REPAIRS", Enter did
  nothing, only the number keys still answered, and J was dead in conversation mode. Now an
  arrow or Enter whose selection sits outside the dialogue UI first lands on what the
  conversation waits on (the first response, or CONTINUE). Enter on a lone CONTINUE also
  advances; a response menu never picks on that anchoring press. J closes a Drive Log left
  open under a conversation. The pause menu keeps its focus.

### Crew selection as a table
- The map's crew window walked by the game's spatial navigation: Up from TRAVEL TO
  CONTRACT reached Juni, Right skipped every crew member, the rig crew (Serafin) could not
  be reached, and a repeat focus on a blocked member lost its name. Its opening read was a
  dump of every label, sometimes with a pre-settle "NO CREW SELECTED".
- New CrewSelectTable: Up and Down walk the drawn crew buttons in the window's own order,
  with their group heading (LOCAL, THE RIG) on crossing, then a crew slots row, then
  TRAVEL TO CONTRACT and BACK. A crew row reads name, status from the button FSM
  (available, selected in slot N, locked in slot N, not available) and the stress and
  skill chips. Enter clicks the member's own SELECT and answers with the result and the
  slots; locked and blocked members say why instead. The window opens with "Select crew.",
  the slots and the first row, half a second after it appears so the button FSMs have
  settled. F1 lists the crew keys. Backspace stays the map's Back.
- Verified live by the player on a real contract: walked Nia, Juni and Serafin, chose
  Nia into slot 2, confirmed the crew.

### Difficulty screen as a list
- The new-game difficulty screen walked by the game's spatial navigation, jumping between
  the three cards, TUTORIALS and CONFIRM in no fixed order. New DifficultyTable: Up and
  Down walk TUTORIALS, SAFE, RISKY, DANGEROUS, CONFIRM DIFFICULTY and BACK. A difficulty
  row reads its name, "Chosen." from the Difficulty Menu FSM state, and its description;
  Enter clicks the card's own SELECT ("SAFE chosen.") or toggles tutorials ("TUTORIALS
  OFF."). The screen opens with the chosen difficulty and the game's note that difficulty
  can be changed any time from the pause menu. Verified live on an empty slot.
- While the difficulty list or the crew table owns its screen, the game's own focus
  landing there (RISKY at open, TRAVEL TO CONTRACT) is no longer read: its late read
  talked over the first table row.

### Repeatable actions
- After an action resolves the game draws "ACTION COMPLETE" on the card while it shows
  the outcome, even though the card takes a new die at once (its Action Controller rests
  in Positive/Neutral/Negative Outcome, each with DiceSlotted -> Reset). Players heard
  "complete", left the location and re-entered it to try again. The row now adds "Enter
  to do it again." when the controller's state accepts a die; Temp Complete and Action
  Completed (no DiceSlotted) stay without the hint.

### "New" on visited locations
- Station and rig rows said "New" for every location on camera, also long after a
  visit. The New Shine object stays active on every drawn marker; the game shows or hides
  it with the Animator bool "New?" (the Location Button FSM sets it from the Lua flag
  `<ID>_OLD` in "Is New?" and clears it in "Set Old" on the first visit). The mod now
  reads that bool, so "New" matches what is drawn. Falls back to the old read when a
  marker has no such bool. Adds a reference to UnityEngine.AnimationModule.

### Die picker with no dice left
- When the die picker opens with every die spent or broken (tray and any rendered crew
  panel), it says "Die picker. No dice left." instead of leaving the player to walk the
  spent cursors. The game still opens the picker as it always does. Parity with the same
  fix in the Citizen Speaker fork.

### Keys during the opening splash
- The engine splash (the logo crawl before the title) runs for about 8 s and cannot be
  skipped; nothing is selectable. A key there said "Nothing to focus." (or nothing at all
  in the first 8 s), as if the game had hung. Any key now says "Loading, please wait.",
  read from SplashScreen.isFinished, which the title FSM's WaitForSplashToFinish waits
  on. F1, Z and F3 keep working. The key that launched the game, landing in the first
  frames, stays silent. Same change as in the Citizen Speaker fork.
- The title's start button says "Press Enter to start" ("Title screen. Press Enter to
  start."). Its label is "CLICK TO START" for the mouse and "PRESS [A] TO START" for a
  gamepad, and the glyph was dropped, so it read "CLICK TO START" or "PRESS TO START".
- The first arrow at the title no longer speaks the button twice: the game re-selects it
  when the input mode switches to gamepad, in the same frame as the dead-end repeat.
- Class select: a Left/Right press during the slide is no longer lost. The game ignores
  input mid-slide, so a quick second press spoke the same class again. The mod now
  speaks the class the press asks for and steps there with the carousel's own event once
  it rests.

### Faster answers to keys
- A dialogue line the player asked for (Enter on Continue, a choice by Enter or number)
  interrupts the line still being read. Tolk_IsSpeaking is unsupported for NVDA and always
  returns false, so Queued lines went straight into NVDA's own queue and the new line had
  to wait behind the old one.
- Key answers are Immediate: die picker open, close, refusal and slotted line; push
  refusal reasons and the confirm box; inventory open and close; character sheet entry and
  confirm; drive log tab names.
- Class select: Left/Right speak the destination at the key press from the carousel FSM
  (was after the slide plus 0.35 s), with "class N of 3"; edges say "First class." /
  "Last class."; a press mid-slide says where it lands.
- Station: Enter right after an arrow, while the camera is still panning, is held and
  pressed once the node is drawn, instead of "Off screen."
- Tutorials: the speech gate opens when the panel starts to fade after CONTINUE, not when
  the fade ends. A key's own Immediate answer is never held by a tutorial.

### New announcements
- The notification feed (new drive, drive complete or failed, new location, new crisis,
  new contract, new crew, contract concluded or failed) is spoken when a card shows.
- The title slot-overwrite warning and the pause quit warnings speak their own text.
- After loading a save or arriving from a journey, the place is spoken ("Hexport.").
- The station/rig swap is named ("Rig." / "Station.").
- After the player closes a tutorial and nothing else speaks, the screen is named.
- The drive log announces opening (tab, number of drives) and closing.
- The options panel is announced when it opens, not at the next key.
- The wake-up summary ends with the rig's clocks ("Clocks: ON THE RUN, 1 of 16."), so
  cycle clocks such as THE HUNT are heard as they move.
- The credits screen speaks its keys: Enter or Backspace skips (the SKIP button was
  gamepad-only), Space reads the credits.
- The DANGEROUS-run end screen ("THE BELT FADES...") is read.
- Item changes outside an action (conversation rewards, contract pay) are spoken from the
  inventory strip's own counts ("RAW SCRAP plus 2, now 2."); outcomes, pushes and the
  cycle transition keep voicing their own.
- Out-of-reach map rows give the game's own requirement words ("Debris field, HAZNAV
  required.", "Patrolled territory, SPOOF TRANSPONDER required.", "Radiation,
  impassable hazard.", "Blockade in effect.").

### Fixes
- Title menus: Backspace goes back (class select, difficulty, slot lists, overwrite
  warning, language) through the MAIN MENU FSM's own Back event.
- The queued-duplicate guard dropped any repeat of the last queued line forever.
- First Down no longer skips the first row (options, character sheet, drive log, station,
  map); the map's first row names its section.
- Drive log Abandon now works (the confirm closed the row and hid the button).
- A choice that ended a conversation no longer keeps Up/Down remapped.
- Back at the title, vitals and the cycle summary start clean for the next game.
- Tutorials: a destroyed panel could keep the speech gate shut (Unity fake null); the
  refocus line after a tutorial was dropped; a chained panel could say "Tutorial still
  open."; prompt text had a space before punctuation, a literal "\n", and "LB" read as
  the B prompt.
- Map: keys during a travel commit no longer reach the game's selection (a second Enter
  could commit twice).
- Push: a quick second P no longer says "not available"; a re-arm after a cancel speaks
  its box.
- Raw object names are no longer spoken for station markers ("Location Button"), the die
  slot on arrival, and the commit button the slotted line already named; a player die's
  "DICE SLOTTED" is not read as a crew verdict.
- Inventory: an end-of-list press keeps the item description.
- Cycle Count is read with Find* (Get* creates a missing variable).
- Enter at the press-start gate after a quit no longer says "Nothing focused."

### Performance
- FSM lookups scanned all ~5000 loaded FSM components on every mod key and on several
  per-frame dials; unique names are now cached and misses retried after 0.5 s.
- The drive log window search ran every frame where no window exists; now once a second.
- The options panel and drive log button are held references.
- The belt map rebuilt several times per key while fuel costs were unpublished; it now
  retries after 0.1 s.
