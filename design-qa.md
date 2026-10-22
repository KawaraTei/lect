# 画像コメント機能の確認（2026-10-22）

## 設計

- 上部の「コメントモード」で各画像の下に入力欄を表示。画像部分は従来の正方形サイズとUniform表示を維持し、コメントが画像に重ならない配置とした。
- 入力欄は約3行、長文は折返しとスクロール。通常のグリッド／リスト選択を保持し、モード解除時に復帰する。
- 一覧とプレビューは同じ `CommentEditor` と `ImageItem.Comment` を使用。保存状態は所属フォルダーの共通状態。上部は操作が最小幅でも収まるよう2段に配置した。
- プレビューは下部に同じ入力欄を表示し、通常ウィンドウでは欄の高さ分を追加する。最大化時はウィンドウ内に収める。

## 実施した確認

- `Tools/CommentSmoke`：日本語・改行の自動保存、日時、再読込、別フォルダーの同名画像、一部画像のみ読込時の既存コメント保持、一覧とプレビューの同期、通常リストへの復帰、リネーム、空欄化、一覧クリア前の保存、壊れたJSONの保護、保存失敗と再試行。`COMMENT_SMOKE_OK`。
- Windows実画面：一覧・プレビューへの日本語入力とJSON反映を確認。コメント内のCtrl+Home / Deleteで文字のみ編集され、画像数が変化しないことを確認。
- WPF描画結果：通常グリッド、コメントグリッド、通常リスト、プレビュー、幅860px・セル96pxを確認。入力欄の切れや重なりなし。長文は入力欄内でスクロール。
- Releaseの自己完結・単一EXEを `publish/LectImageRenamer-comments-win-x64` に出力。

最新の描画証跡は `build-verify/comments-20261022-200127/` の `normal.png`、`comments.png`、`preview.png`、`minimum.png`、`list.png`。Windows入力の確認は同じ表示実装の `comments-20261022-195927` 検証プロセスで実施。

対象機能の表示に未解決のP0/P1/P2なし。日本語IMEの変換途中、異なるDPI、大量画像での性能は未確認。外部ファイル移動の追跡・複数プロセス同時編集は対象外。

## 再実行

```powershell
dotnet run --project Tools/CommentSmoke/CommentSmoke.csproj -p:NuGetAudit=false -p:UseSharedCompilation=false
```

末尾に `-- --interactive` を付けると検証用画像を表示した状態で残す。検証画像・JSON・描画結果は `build-verify` 内に作成し、利用者の画像は使わない。
