# Mission-card reward icons and Ping mark — 2026-10-02

The user's AO screenshot showed empty reward rows after the single-item view
change and a remaining magenta pixel above Accept. The proven shared native
reward list is restored, preserving visible icons and item tooltips. Its small
scroll buttons remain because narrow replacement views hid the reward icons
in the AO client. The Ping texture has one dark pixel so AO does not treat a fully
transparent image as missing; its registered name is used for lookup.

The card reserves more right padding, has three extra pixels of height, and
truncates long titles at a word boundary. Ping and Accept callbacks and reward
data remain unchanged.
AO client inspection is still needed for final visual confirmation.
