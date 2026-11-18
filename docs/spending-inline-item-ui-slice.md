# Create catalogue items while recording purchases

18 November 2026. Each purchase line offers Create item beside its catalogue
selector, for both new purchases and saved purchase corrections. A Material dialog
creates the ordinary spending catalogue item through the existing household setup
endpoint, then adds it to the loaded choices and selects it on the initiating line.
It does not save the purchase or change remembered prices. The user supplies the
actual price after creation; existing purchase header, other lines and manually
entered actual amounts remain intact.

The line's category prefills the dialog's default category. Catalogue creation and
editing now share the same fields component, validation and creation payload with
this dialog, including immutable code/unit identity and natural input heights.
Creating an item remains separate from the purchase: cancelling or clearing the
purchase afterwards does not remove the newly saved catalogue item.

Failed saves retain item fields and show the API error, including duplicate codes.
Cancel/Escape/backdrop protect dirty item drafts. Page navigation, browser Back,
household selection and unload include nested drafts; active item creation blocks
navigation and duplicate writes. Forced context changes close the dialog, and late
responses cannot alter another household's choices or purchase draft. Creation
invalidates older lookup reads so they cannot drop the new option. Newly opened
dialogs have clean validation state and keyboard focus on Item name.

Astra reviewed once and found that disabling the initiating button prevented
Material from restoring keyboard focus. The trigger now stays focusable while
the modal owns focus; the guarded action still prevents duplicate dialogs.
Regression cases assert focus restoration to the same later receipt line after
both cancellation and successful creation. Browser access is
unavailable; rendered Material harness tests cover controls and workflows.

After the review fix, all **351 frontend tests** pass under `TZ=Europe/London`.
Production build and whitespace checks pass. There are no backend, dependency
or schema changes.
