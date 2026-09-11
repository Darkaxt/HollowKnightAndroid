# Dualscreen v3

We are implementing a new UI for the Bottom screen, the goal is to simulate the Silksong
UI more closely, using the same UI elements + some more features.

## Healthbar

We want to move the healthbar from top screen to the bottom screen, look at screenshots.
It should be hidden on the top screen, but a settings toggle should be able to show it
again.

## Tabs

Instead of the top named tabs, we want to take the icons that Silksong uses in its own
menu and align them near the bottom. If possible, we want to do the same as Silksong where
menu icons show up when you unlock the tabs (get your first quest unlocks Tasks tab, get
first map unlocks map tab, etc).

Also, if possible we want a sliding animation between tabs. So when you press on inventory
from tasks screen, it should slide in the content from the left, and if pressing the map
from tasks, it should slide the content in from the right.

## Cursor

There are two different cursors, the selected tab and the highlighted item in each tab.
These should use the same "carets" as we use on the current menu, but preferably we want
also an animation system here, where the caret slides from the previously selected item to
the next. Remember, there are two separate cursors, one for the selected item/task, etc
and one for the selected tab. We should also make the mask icons, nail, etc selectable
with the carets around them if possible.

## Tab contents

Every tab should be reworked to follow the contents of the screenshots. We don't have the
dividers yet from our designer, so we can keep using the white lines we have.

On the crest tab, we need to rotate the two extra tools on the bottom to fix them in.
Ignore the icons being rotated in the screenshot, they should still be upright. Also
disregard the "unequip skills" button text, this menu is not interactable with the
controller, only touch.

## Interactivity

We want to make the Dual screen menu completely replace the in-game menu, this means we
need to be able to consume items, switch tools and crest, and place and remove map markers
from the bottom screen.

For consuming items, we should have just a button on the right side next to the healthbar
which says "USE" or something like that. Preferably only visible when the item is usable.

Same for changing tools, we should have an UNEQUIP/EQUIP button next to the healthbar.
Should only be visible when sitting on a bench, since you can't switch tools when not on a
bench usually.

For the markers, we want a markers button, this button should put us in "marker mode",
this mode should hide the tabs on the bottom and replace them with the unlocked markers
icons. You can then tap one of the marker icons on the bottom to select it, and then tap
the screen to place a marker. When in marker mode, tapping an existing marker on the map
should remove it. The same button should take you out of marker mode. The user should
still be able to pan and zoom the map when in marker mode, so only a single tap should
place/delete marker. When the user enters marker mode, the map should automatically switch
to "full map" mode.
