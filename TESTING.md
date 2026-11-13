# Testing Mun River Power 0.17.6

Close the game, install through `Install.cmd`, and verify `Version: 0.17.6.0`. Use a test city, both while paused and after letting the simulation run.

Check the gates and piers after time advances, and look for `DAM SCENE CREATED` in the log.

Select Mun River Powerhouse and drag an alignment without placing it. Check that the road does not stretch into a large sheet or point skyward. Place it while paused. Compare its road color with the connected game road. Install Automatic Spillway and inspect from above: gates, hydraulic cylinders, sensors, and control cabinets should stay out of the vehicle lane. Sidewalks and curbs should connect at both ends. Let time advance and check road connections and traffic. If anything obstructs traffic or the color differs substantially, send a screenshot and `MunRiverPower.log`.

1. Build several lengths and check the completed building: 75 m should be near 34 MW, 150 m near 68 MW, 225 m near 102 MW, 300 m near 136 MW, and 375 m near 170 MW when head reaches 3 m and efficiency is normal. Game length may differ slightly from the displayed value; also check `length` and `turbines` in the log. Under 75 m should generate no power.
2. Check a length just short of the next 75 m step. Capacity must not rise yet. Extend or edit the alignment through that threshold and check that it does not revert to a vanilla dam.
3. With no flow or less than 3 m of head, output should be zero. Raising budget or workers above 100% should not exceed turbine-count capacity.
4. Save before placement. Compare a vanilla dam and the mod at the same alignment, length, height, and terrain, reloading the same starting point each time. Record the vanilla rating, preview price, and funds before and after confirming placement. Pause to limit unrelated transactions. Do not compare on terrain altered by the previous dam.
5. Check that construction and upkeep ratios are near `min(1, mod rating / vanilla rating)`. Use the same budget period and comparable staffing and use. Compare installed ratings, not actual generation. Waiting through several preview frames should not apply the discount repeatedly.
6. Cancel the mod tool and select the vanilla dam; its price should have no mod discount. Demolish mod pieces and check that a discounted item does not receive an undiscounted refund.
7. Save, close, reopen, and load the city. Check capacity and upkeep again. Back up `ModsData/MunRiverPower` with the city; do not delete `economic-profiles.json`.

The log's `MODULAR POWER APPLIED` entry includes `length`, `turbines`, `rating`, `defaultRating`, `priceRatio`, and `head` for the first building in that cycle. It is a snapshot, not a continuous report.

If results differ, provide screenshots of the building panel, placement price, and funds before and after, plus `MunRiverPower.log`. `Collect-Diagnostics.ps1` can collect this information locally; it uploads nothing automatically.

8. Repeat near 401 m on normal and deeply excavated terrain. Both previews should show 170 MW installed capacity, and estimated generation should not exceed 170 MW. Extending past 450 m should raise the rating to 204 MW. Check that placement, cancellation, and switching to the vanilla dam work normally. With head below 3 m, estimated generation should be about zero while installed capacity remains displayed.

This version attaches Fish Passage and Automatic Spillway meshes to their upgrade prefabs. Use a copy of a city to check that both appear and follow the dam alignment before using them in a main city. Preview generation reflects water while dragging; it may change after construction as water settles.

**Check version 0.8.2 models:** Buy Fish Passage and Automatic Spillway. Their small components should sit beside the powerhouse at the dam end, without forming a wall across the road or sinking into concrete. Try short and long dams in different orientations. If placement is wrong, send top and side screenshots with `MunRiverPower.log`, which records `UPGRADE MODEL ANCHOR` and the original model bounds.

## Fish Passage testing

1. Open a test city with a mod powerhouse from an earlier version. Select its powerhouse and open upgrades. Fish Passage should appear with name, description, icon, and a ₡25,000 price. The vanilla dam should not offer it.
2. Before purchase, there should be no Fish Passage effect. Pause, record funds, install and confirm it on that powerhouse, and check that ₡25,000 is deducted. The same powerhouse must not accept a duplicate. Upkeep should rise ₡500/month before the game's budget multiplier.
3. Let time advance. At 100% powerhouse efficiency, check area happiness near and far: up to +2 near the building, tapering to zero at 4 km. Do not expect citywide average happiness to rise by +2 immediately; residents have other factors and update cycles.
4. Disable and reenable the upgrade, then try removal through the game UI. Its area effect should disappear and return with its state, without a permanent bonus. It should not add turbines or power capacity.
5. Save, close, and load the city. The purchased upgrade should remain, with no second charge or duplicate installation, and resume its effect after simulation runs. Back up `ModsData/MunRiverPower` with the save.
6. Test newly built mod powerhouses at different lengths, especially ones with different `priceRatio` values. Both should offer Fish Passage. Changing the upkeep profile should preserve an installed upgrade.

`MunRiverPower.log` should contain `FISH PASSAGE REGISTERED`. If the upgrade is missing or purchase fails, send the log, upgrade panel screenshot, and version.

Version 0.6.1 check: Hover over and select Fish Passage in the mod powerhouse upgrade panel. Name and description should appear before and after purchase. Change game language and repeat. The English name is `Fish Passage`; the Thai localization adds a Thai translation after that name. Price and effect are unchanged.

## Automatic Spillway 0.7.0 testing — start with a copy of a city

1. For the original 0.7.0 test, close the game, install `0.7.0.0`, and open a copy of a city. A mod powerhouse should show freeboard and `Not installed` status. Its upgrades should include Automatic Spillway with name, description, ₡100,000 price, and ₡1,000/month upkeep. Fish Passage should remain available. For current release testing, install 0.17.6.0 instead.
2. Compare displayed freeboard with the visible water surface and crest. If they disagree, send a screenshot and log before relying on control. The reference level comes from simulated structural data, not a survey of the 3D model.
3. Buy Spillway while freeboard exceeds 1 m; it should stay closed without extra release. Raise water gradually. At 0.5 m freeboard, status should become `Releasing water`, with more water moving from the reservoir downstream.
4. Release should taper near the target and stop when freeboard returns to about 1 m, without staying open until water drops too far. Turbines should still respond to head and available water, without added count or rating.
5. Reload the same starting point with Spillway disabled. Compare upstream and downstream levels over the same time period to distinguish extra release from the game's normal flow. Check that water is not created or lost in excess.
6. Disable the upgrade during release; extra commands should stop. Reenable it at the opening threshold and check that it resumes. Remove the upgrade or powerhouse and check that no persistent source or sink remains.
7. Pause and inspect levels; there should be no extra release while paused. Try normal speed and 10×, checking that the same water reading does not trigger excessive repeated pulses.
8. While the gate is open and freeboard is between 0.5 and 1 m, save, close, and load. The gate should retain its state and release until the closing point, without repurchase or duplicate pieces.
9. If intake/outlet is too dry or downstream water exceeds upstream, the system should wait, without adding water to a dry side or pumping uphill. Check that vanilla dams remain unaffected.

The log should include `AUTOMATIC SPILLWAY REGISTERED`, `SPILLWAY GATE OPEN/CLOSED` with freeboard, upstream level, and crest, and `SPILLWAY TRANSFER SUBMITTED` for the first transfer command. “Submitted” means a command was queued; it does not prove actual water quantities balanced.

If freeboard text is missing, the upgrade cannot be purchased, or water fails to move or moves the wrong way, send screenshots and use `Collect-Diagnostics.ps1` to collect the mod log and tail of `Player.log`.
