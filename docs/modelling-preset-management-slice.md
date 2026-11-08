# Modelling preset management

The modelling page can load, create, update, copy and delete household presets.
Presets save assumptions and selections, never a starting snapshot or its balances.
Loading another preset retains the selected snapshot. A fresh page uses the latest
snapshot and the household's default preset, if one exists. With no default, the
existing editable starting assumptions remain in place.

The Default checkbox is part of the draft and takes effect only on Save changes
or Save preset/Save as new. Saving a default clears the previous default within
the same household and transaction. Unchecking or deleting the default leaves no
default; another preset is not promoted automatically. Other households are unaffected.

`ProjectionPresets.IsDefault` defaults to false for existing rows. A partial unique
index on HouseholdId where IsDefault is true enforces at most one default, even
for direct database writes. The preset service clears an existing default before
writing its replacement, atomically. Serializable retries also handle a conflict
on this specific index when concurrent saves both try to establish a default.
Name conflicts retain their existing 409 response. Create/update requests accept
`isDefault` (omission means false); list/get/write responses include it.

The screen shows unsaved changes, confirms before discarding them, and supports
reverting to the selected saved preset. Save as new requires a distinct name under
the existing name-uniqueness rule. Failed saves preserve the draft. Deleting a
preset keeps current modelling inputs on screen as an unsaved scenario. Controls
are disabled during writes, and stale responses from a previous household are ignored.
No Household Setup page or snapshot changes are introduced.

Retained inputs become unsaved immediately after deletion and remain protected until
saved or explicitly discarded. Leaving the modelling route asks before discarding
unsaved inputs; refresh/close uses the browser's unload warning. An in-flight preset
write blocks route navigation and also requests an unload warning. Changing only the
starting snapshot keeps the inputs and does not prompt. The routes are declared in
`app.routes.ts`, shared by bootstrap and the navigation regression tests.

Checks cover default assignment, reassignment, removal, household scope, atomic
rollback, simultaneous creates/updates, the database constraint, HTTP round trips,
automatic loading, snapshot independence, copied drafts, failed saves, deletion,
and stale responses. PostgreSQL tests use disposable databases.

Verification: 323 API tests, 495 data tests and 95 frontend tests passed. The solution
and production frontend builds passed, and EF reports no pending model changes.
Chromium verified automatic default loading, save-as-new replacement, updates,
snapshot retention, deletion, the no-default fallback and a 430px layout without
horizontal overflow, against an isolated in-memory API with generic sample data.

The subsequent draft-protection review passed all 102 frontend tests, the production
build, and Chromium checks for deleted-draft protection and navigation confirmation.
Unload protection was checked through cancelable `beforeunload` events; browser
refresh/tab-close dialogs were not exercised.
