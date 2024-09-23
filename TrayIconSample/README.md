## Tray icon sample
デスクトップアプリのメニューバーにトレイアイコンを表示するサンプル

### テンプレート
  - `Avalonia Cross Platform App`を使用
  - `Avalonia MVVM App`では`dotnet run`実行時にエラーが出る。
    - 原因調査中

### 実行
```
// ソリューションファイル変更時はclean & buildしないとrun時エラー
$ dotnet clean
$ dotnet build
$ dotnet run
```

