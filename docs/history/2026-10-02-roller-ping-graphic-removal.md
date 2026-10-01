# Remove residual mission-card Ping graphic — 2026-10-02

The user still saw an 8x8 purple dot above Accept after changes to the Ping
texture. Invisible Ping buttons now hide their raised, hover, and pressed
state views directly, without assigning a texture. The unused texture was
removed. Card Ping and the separate Accept callback remain in place.

Reward icons use the restored native item list from the previous correction.
AO client rendering must confirm that the purple dot is gone.
