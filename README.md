# bitXbackup 0.1.0

Windows folder backup utility with an English interface, powered by 7-Zip.

Extract the package and run **Build.cmd**. It compiles the EXE and runs core tests. Afterwards only `bitXbackup.exe` is needed; it embeds 7z.exe, 7z.dll and its icon. A matching external 7z.exe/7z.dll pair beside the application takes priority.

## Use

Select Desktop, Documents, Downloads, OpenVPN or WireGuard and add any extra folders. Existing personal folders start selected. VPN folders are detected but remain optional. Choose an empty destination field using Browse. Optionally enable encryption, enter and confirm a password, then click Create backup.

The resulting 7z archive contains numbered source folders and an original-paths.txt mapping. Nested selections are consolidated to avoid backing up the same folder twice. Encryption hides both contents and filenames. The archive is tested before it is published. Temporary plaintext copies are deleted after success, cancellation or failure; cleanup errors are displayed.

Import/export saves quick-folder selections, custom paths and the encryption checkbox. It never saves the archive password or the contents of VPN profiles. Quick folders resolve on the current PC when importing; custom paths stay unchanged.

## Permissions and limitations

- Run as administrator is available for protected folders such as WireGuard configurations. It opens a fresh instance; unsaved selections are not transferred.
- WireGuard protected profiles are backed up in their original encrypted form. They may depend on the original machine/account and are not a portable VPN export.
- Unreadable or locked files and directory/file links cause the backup to fail. This version has no VSS snapshot support; close apps that modify selected files first.
- These are file-content backups, not full Windows images. ACLs, alternate streams, special filesystem metadata and live database consistency are not preserved.
- Destination must be outside the source folders. Space is needed for a plaintext staging copy plus the final archive.
- The password is passed to the 7-Zip process using its command-line password switch. Administrators and process monitoring can observe it. It is not saved in settings or application logs.
- This tool does not upload to a cloud service. Copy the completed archive to your approved destination.
- Windows compilation, GUI and actual 7-Zip execution have not yet been tested in the authoring environment.

About includes GitHub, [bitx.ro](https://bitx.ro) and [Support via Revolut](https://revolut.me/bin).

## Third-party software

7-Zip is a separate project by Igor Pavlov. See https://www.7-zip.org/license.txt for its LGPL/BSD/unRAR licensing terms and https://www.7-zip.org/ for source downloads. Supplied 7-Zip binaries are used unchanged. Do not remove third-party notices when redistributing.
