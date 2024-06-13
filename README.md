# KOTH (King of the Hill) Plugin

The KOTH (King of the Hill) plugin for Rust allows server administrators to create and manage King of the Hill events. Participants compete to capture and hold a designated area to earn points. The player with the most points at the end of the event wins and gains access to a hackable locked crate containing configurable items. Participants can also earn points by killing other players within the event zone.

## Features

- Create KOTH events at specified locations or at the admin's current location.
- Automatically start KOTH events at a configurable interval.
- Award points to participants for staying in the event zone and for killing other players.
- Teleport players near the KOTH event with a command.
- Reward the winner with access to a hackable locked crate containing configurable items.
- Prevent non-winners and non-participants from looting the crate.
- Ensure the hackable locked crate cannot be damaged.
- Display a visible barrier around the event zone.

## Commands

- `/kothcreate <x> <y> <z> <radius>`: Creates a KOTH event at the specified coordinates with the given radius. (Admin only)
- `/kothcreatehere <radius>`: Creates a KOTH event at the admin's current location with the given radius. (Admin only)
- `/kothstop`: Stops the current KOTH event. (Admin only)
- `/joinkoth`: Teleports the player near the KOTH event.

## Permissions

- `koth.admin`: Grants permission to use admin commands for managing KOTH events.

## Configuration

The plugin configuration allows customization of the items in the hackable locked crate and the event scheduling. The default configuration includes:

```json
{
    "CrateItems": [
        "rifle.ak",
        "ammo.rifle"
    ],
    "EventDuration": 600,
    "EventInterval": 3600
}
