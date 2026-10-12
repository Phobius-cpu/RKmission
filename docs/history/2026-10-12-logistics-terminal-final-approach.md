# Logistics terminal final approach

AO showed a verified Scottyboi landing in Old Athen followed by a stationary
30-second failure to live-verify the saved mission terminal and surveyed `oa`
origin. Return navigation stopped within 6 m of the saved terminal. Because the
surveyed route origin is offset from that saved position, the character could
still be outside the origin verifier's separate 6 m radius.

The automatic return now approaches within 2 m of the saved mission terminal
before beginning surveyed-origin verification. Exact terminal identity,
expected surveyed position, correct playfield, and unique persisted site remain
required. The existing 30-second fail-closed timeout is preserved.

Waiting and terminal failure diagnostics now include the player's distance to
the saved terminal, distance to the surveyed origin, and exact-terminal
visibility/position evidence. The focused regression and Release build pass.
The user will perform AO runtime validation; no successful live logistics cycle
is claimed by this change.
