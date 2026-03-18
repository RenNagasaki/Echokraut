using System.Collections.Generic;
using Dalamud.Game;

namespace Echokraut.Localization;

/// <summary>
/// Localization lookup. The English text is the dictionary key; non-English
/// translations are stored per <see cref="ClientLanguage"/>.
/// Call <see cref="Init"/> once at startup, then <see cref="S"/> everywhere.
/// </summary>
public static class Loc
{
    private static ClientLanguage _lang = ClientLanguage.English;

    public static void Init(ClientLanguage lang) => _lang = lang;

    /// <summary>Returns the translated string for the current client language.
    /// Falls back to <paramref name="key"/> (the English text) if no translation exists.</summary>
    public static string S(string key) =>
        _lang != ClientLanguage.English
        && Translations.TryGetValue(key, out var map)
        && map.TryGetValue(_lang, out var val)
            ? val
            : key;

    // Shorthand aliases used by many files
    private static readonly ClientLanguage DE = ClientLanguage.German;
    private static readonly ClientLanguage FR = ClientLanguage.French;
    private static readonly ClientLanguage JP = ClientLanguage.Japanese;

    // ─────────────────────────────────────────────────────────────────────
    // Translations — key = English text, value = { lang → translation }
    // ─────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, Dictionary<ClientLanguage, string>> Translations = new()
    {
        // ── Top-level tabs ────────────────────────────────────────────────
        ["Settings"] = new() { { DE, "Einstellungen" }, { FR, "Paramètres" }, { JP, "設定" } },
        ["Voice Sel."] = new() { { DE, "Stimmen" }, { FR, "Voix" }, { JP, "ボイス選択" } },
        ["Voice selection"] = new() { { DE, "Stimmenauswahl" }, { FR, "Sélection de voix" }, { JP, "ボイス選択" } },
        ["Phonetics"] = new() { { DE, "Phonetik" }, { FR, "Phonétique" }, { JP, "音声修正" } },
        ["Phonetic corrections"] = new() { { DE, "Phonetische Korrekturen" }, { FR, "Corrections phonétiques" }, { JP, "音声修正" } },
        ["Logs"] = new() { { DE, "Protokoll" }, { FR, "Journaux" }, { JP, "ログ" } },

        // ── Settings sub-tabs ─────────────────────────────────────────────
        ["General"] = new() { { DE, "Allgemein" }, { FR, "Général" }, { JP, "全般" } },
        ["Dialogue"] = new() { { DE, "Dialog" }, { FR, "Dialogue" }, { JP, "ダイアログ" } },
        ["Chat"] = new() { { DE, "Chat" }, { FR, "Chat" }, { JP, "チャット" } },
        ["Storage"] = new() { { DE, "Speicher" }, { FR, "Stockage" }, { JP, "保存" } },
        ["Backend"] = new() { { DE, "Backend" }, { FR, "Backend" }, { JP, "バックエンド" } },

        // ── Voice selection sub-tabs ──────────────────────────────────────
        ["NPCs"] = new() { { DE, "NPCs" }, { FR, "PNJ" }, { JP, "NPC" } },
        ["Players"] = new() { { DE, "Spieler" }, { FR, "Joueurs" }, { JP, "プレイヤー" } },
        ["Bubbles"] = new() { { DE, "Sprechblasen" }, { FR, "Bulles" }, { JP, "吹き出し" } },
        ["Voices"] = new() { { DE, "Stimmen" }, { FR, "Voix" }, { JP, "ボイス" } },

        // ── Section headers ───────────────────────────────────────────────
        ["In-Game Controls:"] = new() { { DE, "Spielinterne Steuerung:" }, { FR, "Contrôles en jeu :" }, { JP, "ゲーム内コントロール:" } },
        ["In-Game Controls"] = new() { { DE, "Spielinterne Steuerung" }, { FR, "Contrôles en jeu" }, { JP, "ゲーム内コントロール" } },
        ["Reset Data"] = new() { { DE, "Daten zurücksetzen" }, { FR, "Réinitialiser les données" }, { JP, "データリセット" } },
        ["Reset Data:"] = new() { { DE, "Daten zurücksetzen:" }, { FR, "Réinitialiser les données :" }, { JP, "データリセット:" } },
        ["Battle Dialogue"] = new() { { DE, "Kampfdialog" }, { FR, "Dialogue de combat" }, { JP, "バトルダイアログ" } },
        ["NPC Bubbles"] = new() { { DE, "NPC-Sprechblasen" }, { FR, "Bulles PNJ" }, { JP, "NPC吹き出し" } },
        ["Google Drive"] = new() { { DE, "Google Drive" }, { FR, "Google Drive" }, { JP, "Google Drive" } },
        ["3D Space"] = new() { { DE, "3D-Raum" }, { FR, "Espace 3D" }, { JP, "3D空間" } },
        ["3D Space Debug Info"] = new() { { DE, "3D-Raum Debuginfo" }, { FR, "Infos de débogage 3D" }, { JP, "3D空間デバッグ情報" } },
        ["Chat channels"] = new() { { DE, "Chat-Kanäle" }, { FR, "Canaux de chat" }, { JP, "チャットチャンネル" } },
        ["Instance type"] = new() { { DE, "Instanztyp" }, { FR, "Type d'instance" }, { JP, "インスタンスタイプ" } },
        ["Local instance"] = new() { { DE, "Lokale Instanz" }, { FR, "Instance locale" }, { JP, "ローカルインスタンス" } },
        ["Advanced options"] = new() { { DE, "Erweiterte Optionen" }, { FR, "Options avancées" }, { JP, "詳細オプション" } },
        ["Remote connection"] = new() { { DE, "Remote-Verbindung" }, { FR, "Connexion distante" }, { JP, "リモート接続" } },
        ["Service options"] = new() { { DE, "Dienstoptionen" }, { FR, "Options du service" }, { JP, "サービスオプション" } },
        ["Filter Options"] = new() { { DE, "Filteroptionen" }, { FR, "Options de filtre" }, { JP, "フィルターオプション" } },
        ["Advanced Filters"] = new() { { DE, "Erweiterte Filter" }, { FR, "Filtres avancés" }, { JP, "詳細フィルター" } },
        ["Install process:"] = new() { { DE, "Installation:" }, { FR, "Processus d'installation :" }, { JP, "インストール:" } },
        ["Available commands"] = new() { { DE, "Verfügbare Befehle" }, { FR, "Commandes disponibles" }, { JP, "利用可能なコマンド" } },

        // ── General settings ──────────────────────────────────────────────
        ["Enabled"] = new() { { DE, "Aktiviert" }, { FR, "Activé" }, { JP, "有効" } },
        ["Use native FFXIV UI"] = new() { { DE, "Native FFXIV-Oberfläche verwenden" }, { FR, "Utiliser l'interface native FFXIV" }, { JP, "ネイティブFFXIV UIを使用" } },
        ["Generate per sentence (shorter latency, recommended for CPU inference)"] = new()
        {
            { DE, "Pro Satz generieren (kürzere Latenz, empfohlen für CPU-Inferenz)" },
            { FR, "Générer par phrase (latence réduite, recommandé pour l'inférence CPU)" },
            { JP, "文ごとに生成（低遅延、CPU推論向け）" },
        },
        ["Remove stutters"] = new() { { DE, "Stottern entfernen" }, { FR, "Supprimer les bégaiements" }, { JP, "吃音を除去" } },
        ["Hide UI in cutscenes"] = new() { { DE, "UI in Zwischensequenzen ausblenden" }, { FR, "Masquer l'interface en cinématique" }, { JP, "カットシーン中にUIを非表示" } },
        ["Remove punctuation (may reduce speech hallucinations)"] = new()
        {
            { DE, "Satzzeichen entfernen (kann Sprachfehler reduzieren)" },
            { FR, "Supprimer la ponctuation (peut réduire les artefacts vocaux)" },
            { JP, "句読点を除去（音声異常の低減に役立つ場合があります）" },
        },
        ["Show Play/Pause, Stop and Mute buttons in dialogue"] = new()
        {
            { DE, "Play/Pause-, Stopp- und Stummschaltung-Buttons im Dialog anzeigen" },
            { FR, "Afficher les boutons Lecture/Pause, Arrêt et Muet dans les dialogues" },
            { JP, "ダイアログに再生/一時停止、停止、ミュートボタンを表示" },
        },
        ["Show extended options (voice selector, auto-advance)"] = new()
        {
            { DE, "Erweiterte Optionen anzeigen (Stimmenwahl, Auto-Weiter)" },
            { FR, "Afficher les options avancées (sélection vocale, avance auto)" },
            { JP, "拡張オプションを表示（ボイス選択、自動進行）" },
        }, 

        // ── Dialogue settings ─────────────────────────────────────────────
        ["Voice dialogue"] = new() { { DE, "Dialog vertonen" }, { FR, "Dialogues vocaux" }, { JP, "ダイアログをボイス化" } },
        ["Voice dialogue in 3D space"] = new() { { DE, "Dialog im 3D-Raum vertonen" }, { FR, "Dialogues vocaux en 3D" }, { JP, "3D空間でダイアログをボイス化" } },
        ["Voice player choices in cutscenes"] = new() { { DE, "Spielerauswahl in Zwischensequenzen vertonen" }, { FR, "Voix des choix du joueur en cinématique" }, { JP, "カットシーンでプレイヤーの選択肢をボイス化" } },
        ["Voice player choices outside cutscenes"] = new() { { DE, "Spielerauswahl außerhalb von Zwischensequenzen vertonen" }, { FR, "Voix des choix du joueur hors cinématique" }, { JP, "カットシーン外でプレイヤーの選択肢をボイス化" } },
        ["Cancel voice on text advance"] = new() { { DE, "Stimme bei Textfortschritt abbrechen" }, { FR, "Annuler la voix lors de l'avancement du texte" }, { JP, "テキスト送り時にボイスを中止" } },
        ["Auto-advance dialogue after speech completes"] = new() { { DE, "Dialog nach Sprachausgabe automatisch weiterschalten" }, { FR, "Avancer le dialogue automatiquement après la lecture" }, { JP, "音声完了後にダイアログを自動進行" } },
        ["Voice retainer dialogue"] = new() { { DE, "Gehilfendialoge vertonen" }, { FR, "Dialogues de servants vocaux" }, { JP, "リテイナーダイアログをボイス化" } },

        // ── Battle dialogue ───────────────────────────────────────────────
        ["Voice battle dialogue"] = new() { { DE, "Kampfdialoge vertonen" }, { FR, "Dialogues de combat vocaux" }, { JP, "バトルダイアログをボイス化" } },
        ["Queue battle dialogue"] = new() { { DE, "Kampfdialoge in Warteschlange" }, { FR, "Mettre en file les dialogues de combat" }, { JP, "バトルダイアログをキューに入れる" } },

        // ── Bubbles ───────────────────────────────────────────────────────
        ["Voice NPC bubbles"] = new() { { DE, "NPC-Sprechblasen vertonen" }, { FR, "Bulles PNJ vocales" }, { JP, "NPC吹き出しをボイス化" } },
        ["Voice bubbles in cities"] = new() { { DE, "Sprechblasen in Städten vertonen" }, { FR, "Bulles vocales en ville" }, { JP, "街中の吹き出しをボイス化" } },
        ["Use camera as 3D sound source"] = new() { { DE, "Kamera als 3D-Klangquelle verwenden" }, { FR, "Utiliser la caméra comme source sonore 3D" }, { JP, "カメラを3D音源として使用" } },

        // ── Chat ──────────────────────────────────────────────────────────
        ["Voice chat"] = new() { { DE, "Chat vertonen" }, { FR, "Chat vocal" }, { JP, "チャットをボイス化" } },
        ["Voice chat in 3D space"] = new() { { DE, "Chat im 3D-Raum vertonen" }, { FR, "Chat vocal en 3D" }, { JP, "3D空間でチャットをボイス化" } },
        ["Voice your own chat"] = new() { { DE, "Eigenen Chat vertonen" }, { FR, "Voix de votre propre chat" }, { JP, "自分のチャットをボイス化" } },
        ["Voice Say"] = new() { { DE, "Sagen vertonen" }, { FR, "Dire vocal" }, { JP, "Sayをボイス化" } },
        ["Voice Yell"] = new() { { DE, "Rufen vertonen" }, { FR, "Crier vocal" }, { JP, "Yellをボイス化" } },
        ["Voice Shout"] = new() { { DE, "Schreien vertonen" }, { FR, "Hurler vocal" }, { JP, "Shoutをボイス化" } },
        ["Voice Free Company"] = new() { { DE, "Freie Gesellschaft vertonen" }, { FR, "Compagnie libre vocal" }, { JP, "FCをボイス化" } },
        ["Voice Tell"] = new() { { DE, "Flüstern vertonen" }, { FR, "Chuchotement vocal" }, { JP, "Tellをボイス化" } },
        ["Voice Party"] = new() { { DE, "Gruppe vertonen" }, { FR, "Groupe vocal" }, { JP, "Partyをボイス化" } },
        ["Voice Alliance"] = new() { { DE, "Allianz vertonen" }, { FR, "Alliance vocal" }, { JP, "Allianceをボイス化" } },
        ["Voice Novice Network"] = new() { { DE, "Neulingschat vertonen" }, { FR, "Réseau des novices vocal" }, { JP, "ビギナーチャンネルをボイス化" } },
        ["Voice linkshells"] = new() { { DE, "Linkshells vertonen" }, { FR, "Linkshells vocal" }, { JP, "リンクシェルをボイス化" } },
        ["Voice cross-world linkshells"] = new() { { DE, "Weltenübergreifende Linkshells vertonen" }, { FR, "Linkshells inter-mondes vocal" }, { JP, "クロスワールドリンクシェルをボイス化" } },
        ["DetectLanguage automatically detects the language of chat messages. Register for a free API key at:"] = new()
        {
            { DE, "DetectLanguage erkennt die Sprache von Chat-Nachrichten automatisch. Registrieren Sie sich kostenlos unter:" },
            { FR, "DetectLanguage détecte automatiquement la langue des messages. Inscrivez-vous gratuitement sur :" },
            { JP, "DetectLanguageはチャットメッセージの言語を自動検出します。無料のAPIキーはこちらから取得できます:" },
        },
        ["Detect Language API Key"] = new() { { DE, "DetectLanguage API-Schlüssel" }, { FR, "Clé API DetectLanguage" }, { JP, "DetectLanguage APIキー" } },
        ["Detect Language API key (detectlanguage.com)"] = new() { { DE, "DetectLanguage API-Schlüssel (detectlanguage.com)" }, { FR, "Clé API DetectLanguage (detectlanguage.com)" }, { JP, "DetectLanguage APIキー (detectlanguage.com)" } },

        // ── 3D slider ─────────────────────────────────────────────────────
        ["3D audible range (shared) — higher = shorter range, 0 = on player"] = new()
        {
            { DE, "3D-Hörbereich (geteilt) — höher = kürzere Reichweite, 0 = am Spieler" },
            { FR, "Portée audible 3D (partagée) — plus élevé = portée courte, 0 = sur le joueur" },
            { JP, "3D可聴範囲（共有）— 高い＝範囲が狭い、0＝プレイヤー位置" },
        },

        // ── Storage / Save settings ───────────────────────────────────────
        ["Search audio locally before generating"] = new() { { DE, "Audio lokal suchen bevor generiert wird" }, { FR, "Rechercher l'audio localement avant de générer" }, { JP, "生成前にローカルで音声を検索" } },
        ["Search for audio locally before generating"] = new() { { DE, "Audio lokal suchen bevor generiert wird" }, { FR, "Rechercher l'audio localement avant de générer" }, { JP, "生成前にローカルで音声を検索" } },
        ["Save generated audio locally"] = new() { { DE, "Generiertes Audio lokal speichern" }, { FR, "Enregistrer l'audio généré localement" }, { JP, "生成した音声をローカルに保存" } },
        ["Create directory if it doesn't exist"] = new() { { DE, "Verzeichnis erstellen, falls nicht vorhanden" }, { FR, "Créer le dossier s'il n'existe pas" }, { JP, "ディレクトリが存在しない場合は作成" } },
        ["Create directory if missing"] = new() { { DE, "Verzeichnis erstellen, falls nicht vorhanden" }, { FR, "Créer le dossier s'il n'existe pas" }, { JP, "ディレクトリが存在しない場合は作成" } },
        ["Local audio directory path"] = new() { { DE, "Lokaler Audio-Verzeichnispfad" }, { FR, "Chemin du dossier audio local" }, { JP, "ローカル音声ディレクトリパス" } },
        ["Helping build a voice line database benefits everyone. Please consider opting in."] = new()
        {
            { DE, "Der Aufbau einer Sprachzeilen-Datenbank nützt allen. Bitte erwägen Sie teilzunehmen." },
            { FR, "Contribuer à la base de données vocale profite à tous. Merci d'envisager de participer." },
            { JP, "ボイスラインデータベースの構築は全員の役に立ちます。ぜひご協力ください。" },
        },
        ["Send dialogue lines to Ren Nagasaki's share for a voice line database"] = new()
        {
            { DE, "Dialogzeilen an Ren Nagasakis Share für eine Sprachzeilen-Datenbank senden" },
            { FR, "Envoyer les lignes de dialogue au partage de Ren Nagasaki pour une base vocale" },
            { JP, "ボイスラインデータベース用にRen Nagasakiの共有フォルダに台詞を送信" },
        },
        ["Upload to Google Drive (requires local save)"] = new()
        {
            { DE, "Auf Google Drive hochladen (erfordert lokale Speicherung)" },
            { FR, "Téléverser sur Google Drive (nécessite la sauvegarde locale)" },
            { JP, "Google Driveにアップロード（ローカル保存が必要）" },
        },
        ["Download from Google Drive share"] = new() { { DE, "Von Google Drive Share herunterladen" }, { FR, "Télécharger depuis le partage Google Drive" }, { JP, "Google Drive共有からダウンロード" } },
        ["Download periodically (every 60 min, new files only)"] = new()
        {
            { DE, "Regelmäßig herunterladen (alle 60 Min., nur neue Dateien)" },
            { FR, "Télécharger périodiquement (toutes les 60 min, nouveaux fichiers uniquement)" },
            { JP, "定期的にダウンロード（60分ごと、新規ファイルのみ）" },
        },
        ["Google Drive share link"] = new() { { DE, "Google Drive Share-Link" }, { FR, "Lien de partage Google Drive" }, { JP, "Google Drive共有リンク" } },
        ["Download now"] = new() { { DE, "Jetzt herunterladen" }, { FR, "Télécharger maintenant" }, { JP, "今すぐダウンロード" } },

        // ── Backend / Alltalk ─────────────────────────────────────────────
        ["Local instance (runs on your GPU)"] = new() { { DE, "Lokale Instanz (nutzt Ihre GPU)" }, { FR, "Instance locale (utilise votre GPU)" }, { JP, "ローカルインスタンス（GPUで実行）" } },
        ["Remote instance (connect to a server)"] = new() { { DE, "Remote-Instanz (Verbindung zu einem Server)" }, { FR, "Instance distante (connexion à un serveur)" }, { JP, "リモートインスタンス（サーバーに接続）" } },
        ["No instance (audio files only, no generation)"] = new() { { DE, "Keine Instanz (nur Audiodateien, keine Generierung)" }, { FR, "Aucune instance (fichiers audio uniquement)" }, { JP, "インスタンスなし（音声ファイルのみ、生成なし）" } },
        ["Remote instance"] = new() { { DE, "Remote-Instanz" }, { FR, "Instance distante" }, { JP, "リモートインスタンス" } },
        ["No instance"] = new() { { DE, "Keine Instanz" }, { FR, "Aucune instance" }, { JP, "インスタンスなし" } },
        ["Streaming generation (play audio before full text is generated)"] = new()
        {
            { DE, "Streaming-Generierung (Audio abspielen, bevor der gesamte Text generiert ist)" },
            { FR, "Génération en streaming (lecture avant la fin de la génération)" },
            { JP, "ストリーミング生成（全文生成前に音声を再生）" },
        },
        ["Auto-start local instance on plugin load"] = new() { { DE, "Lokale Instanz beim Laden des Plugins automatisch starten" }, { FR, "Démarrer l'instance locale au chargement du plugin" }, { JP, "プラグイン読み込み時にローカルインスタンスを自動起動" } },
        ["Is Windows 11"] = new() { { DE, "Windows 11" }, { FR, "Windows 11" }, { JP, "Windows 11" } },
        ["Local install path (no spaces or dashes)"] = new() { { DE, "Lokaler Installationspfad (keine Leerzeichen oder Bindestriche)" }, { FR, "Chemin d'installation local (sans espaces ni tirets)" }, { JP, "ローカルインストールパス（スペースやダッシュなし）" } },
        ["Custom model URL (zip with one root folder)"] = new() { { DE, "Benutzerdefinierte Modell-URL (ZIP mit einem Stammordner)" }, { FR, "URL du modèle personnalisé (zip avec un dossier racine)" }, { JP, "カスタムモデルURL（ルートフォルダ1つのzip）" } },
        ["Custom voices URL (zip with \"voices\" folder)"] = new() { { DE, "Benutzerdefinierte Stimmen-URL (ZIP mit \"voices\"-Ordner)" }, { FR, "URL des voix personnalisées (zip avec dossier \"voices\")" }, { JP, "カスタムボイスURL（「voices」フォルダ付きzip）" } },
        ["Install only custom data"] = new() { { DE, "Nur benutzerdefinierte Daten installieren" }, { FR, "Installer uniquement les données personnalisées" }, { JP, "カスタムデータのみインストール" } },
        ["Install"] = new() { { DE, "Installieren" }, { FR, "Installer" }, { JP, "インストール" } },
        ["Reinstall"] = new() { { DE, "Neuinstallieren" }, { FR, "Réinstaller" }, { JP, "再インストール" } },
        ["Installing..."] = new() { { DE, "Installiere..." }, { FR, "Installation..." }, { JP, "インストール中..." } },
        ["Reinstall (removes existing and installs fresh)"] = new() { { DE, "Neuinstallieren (bestehende Installation löschen und neu installieren)" }, { FR, "Réinstaller (supprime l'existant et installe à neuf)" }, { JP, "再インストール（既存を削除して新規インストール）" } },
        ["Start"] = new() { { DE, "Starten" }, { FR, "Démarrer" }, { JP, "開始" } },
        ["Stop"] = new() { { DE, "Stoppen" }, { FR, "Arrêter" }, { JP, "停止" } },
        ["Starting..."] = new() { { DE, "Starte..." }, { FR, "Démarrage..." }, { JP, "起動中..." } },
        ["Stopping..."] = new() { { DE, "Stoppe..." }, { FR, "Arrêt..." }, { JP, "停止中..." } },
        ["Running"] = new() { { DE, "Läuft" }, { FR, "En cours" }, { JP, "実行中" } },
        ["Test"] = new() { { DE, "Testen" }, { FR, "Tester" }, { JP, "テスト" } },
        ["Testing..."] = new() { { DE, "Teste..." }, { FR, "Test en cours..." }, { JP, "テスト中..." } },
        ["Reload model"] = new() { { DE, "Modell neu laden" }, { FR, "Recharger le modèle" }, { JP, "モデルを再読み込み" } },
        ["Reload voices"] = new() { { DE, "Stimmen neu laden" }, { FR, "Recharger les voix" }, { JP, "ボイスを再読み込み" } },
        ["Alltalk base URL"] = new() { { DE, "Alltalk Basis-URL" }, { FR, "URL de base Alltalk" }, { JP, "Alltalk ベースURL" } },
        ["Model name to reload"] = new() { { DE, "Modellname zum Neuladen" }, { FR, "Nom du modèle à recharger" }, { JP, "再読み込みするモデル名" } },
        ["Model to reload"] = new() { { DE, "Modellname zum Neuladen" }, { FR, "Nom du modèle à recharger" }, { JP, "再読み込みするモデル名" } },
        ["Local instance path:"] = new() { { DE, "Pfad der lokalen Instanz:" }, { FR, "Chemin de l'instance locale :" }, { JP, "ローカルインスタンスパス:" } },

        // ── Alltalk error/info texts ──────────────────────────────────────
        ["The Alltalk path must not contain spaces or dashes.\r\nPlease make sure it's formatted correctly."] = new()
        {
            { DE, "Der Alltalk-Pfad darf keine Leerzeichen oder Bindestriche enthalten.\r\nBitte stellen Sie sicher, dass er korrekt formatiert ist." },
            { FR, "Le chemin Alltalk ne doit pas contenir d'espaces ni de tirets.\r\nVeuillez vérifier le format." },
            { JP, "Alltalkのパスにスペースやダッシュを含めることはできません。\r\n正しい形式になっているか確認してください。" },
        },
        ["The Alltalk path must not be empty.\r\nPlease enter a valid path."] = new()
        {
            { DE, "Der Alltalk-Pfad darf nicht leer sein.\r\nBitte geben Sie einen gültigen Pfad ein." },
            { FR, "Le chemin Alltalk ne doit pas être vide.\r\nVeuillez entrer un chemin valide." },
            { JP, "Alltalkのパスを空にすることはできません。\r\n有効なパスを入力してください。" },
        },
        ["The CUDA Toolkit does not appear to be installed.\r\nIt is required for local Alltalk instances."] = new()
        {
            { DE, "Das CUDA Toolkit scheint nicht installiert zu sein.\r\nEs wird für lokale Alltalk-Instanzen benötigt." },
            { FR, "Le CUDA Toolkit ne semble pas être installé.\r\nIl est requis pour les instances locales Alltalk." },
            { JP, "CUDA Toolkitがインストールされていないようです。\r\nローカルAlltalkインスタンスに必要です。" },
        },
        ["Custom XTTS model URL (zip file with all files in one root folder):"] = new()
        {
            { DE, "Benutzerdefinierte XTTS-Modell-URL (ZIP-Datei mit einem Stammordner):" },
            { FR, "URL du modèle XTTS personnalisé (fichier zip avec un dossier racine) :" },
            { JP, "カスタムXTTSモデルURL（ルートフォルダ1つのzipファイル）:" },
        },
        ["Custom voices URL (zip file with a \"voices\" root folder):"] = new()
        {
            { DE, "Benutzerdefinierte Stimmen-URL (ZIP-Datei mit \"voices\"-Stammordner):" },
            { FR, "URL des voix personnalisées (fichier zip avec dossier racine \"voices\") :" },
            { JP, "カスタムボイスURL（「voices」ルートフォルダ付きzipファイル）:" },
        },
        ["The installation requires about 20 GB of disk space and may take a while depending on your connection."] = new()
        {
            { DE, "Die Installation benötigt ca. 20 GB Speicherplatz und kann je nach Verbindung einige Zeit dauern." },
            { FR, "L'installation nécessite environ 20 Go d'espace disque et peut prendre du temps selon votre connexion." },
            { JP, "インストールには約20GBのディスク容量が必要で、接続速度によっては時間がかかる場合があります。" },
        },
        ["Up to two shell windows may open during installation — you can follow the progress there."] = new()
        {
            { DE, "Während der Installation können bis zu zwei Konsolenfenster geöffnet werden — dort können Sie den Fortschritt verfolgen." },
            { FR, "Jusqu'à deux fenêtres de console peuvent s'ouvrir pendant l'installation — vous pouvez suivre la progression." },
            { JP, "インストール中に最大2つのシェルウィンドウが開く場合があります。そこで進捗を確認できます。" },
        },
        ["You are not on Windows. Additional setup steps are required to use Alltalk locally."] = new()
        {
            { DE, "Sie verwenden nicht Windows. Zusätzliche Einrichtungsschritte sind erforderlich, um Alltalk lokal zu nutzen." },
            { FR, "Vous n'êtes pas sous Windows. Des étapes supplémentaires sont nécessaires pour utiliser Alltalk localement." },
            { JP, "Windowsではありません。Alltalkをローカルで使用するには追加のセットアップ手順が必要です。" },
        },
        ["Please refer to the Discord or the install instructions from erew123 (link above)."] = new()
        {
            { DE, "Bitte beachten Sie den Discord oder die Installationsanweisungen von erew123 (Link oben)." },
            { FR, "Veuillez consulter le Discord ou les instructions d'installation d'erew123 (lien ci-dessus)." },
            { JP, "Discordまたはerew123のインストール手順（上のリンク）をご参照ください。" },
        },
        ["If you have already completed these steps, you can ignore this message."] = new()
        {
            { DE, "Wenn Sie diese Schritte bereits abgeschlossen haben, können Sie diese Meldung ignorieren." },
            { FR, "Si vous avez déjà effectué ces étapes, vous pouvez ignorer ce message." },
            { JP, "これらの手順を完了済みの場合は、このメッセージを無視してください。" },
        },
        ["No audio will be generated in this mode."] = new()
        {
            { DE, "In diesem Modus wird kein Audio generiert." },
            { FR, "Aucun audio ne sera généré dans ce mode." },
            { JP, "このモードでは音声は生成されません。" },
        },
        ["Only use this if you are unable to run Alltalk at all."] = new()
        {
            { DE, "Verwenden Sie dies nur, wenn Sie Alltalk überhaupt nicht ausführen können." },
            { FR, "N'utilisez ceci que si vous ne pouvez pas du tout exécuter Alltalk." },
            { JP, "Alltalkを全く使用できない場合にのみ使用してください。" },
        },
        ["You will need to obtain audio files from a friend or via a Google Drive share link."] = new()
        {
            { DE, "Sie müssen Audiodateien von einem Freund oder über einen Google Drive Share-Link besorgen." },
            { FR, "Vous devrez obtenir les fichiers audio d'un ami ou via un lien de partage Google Drive." },
            { JP, "友人またはGoogle Drive共有リンクから音声ファイルを入手する必要があります。" },
        },

        // ── First Time Wizard ─────────────────────────────────────────────
        ["Welcome to Echokraut!"] = new() { { DE, "Willkommen bei Echokraut!" }, { FR, "Bienvenue dans Echokraut !" }, { JP, "Echokrautへようこそ！" } },
        ["This plugin gives nearly every text in the game a voice using Alltalk TTS."] = new()
        {
            { DE, "Dieses Plugin gibt fast jedem Text im Spiel eine Stimme mittels Alltalk TTS." },
            { FR, "Ce plugin donne une voix à presque tous les textes du jeu grâce à Alltalk TTS." },
            { JP, "このプラグインはAlltalk TTSを使用して、ゲーム内のほぼすべてのテキストにボイスを付けます。" },
        },
        ["Choose how you want to set up text-to-speech:"] = new()
        {
            { DE, "Wählen Sie aus, wie Sie die Sprachausgabe einrichten möchten:" },
            { FR, "Choisissez comment configurer la synthèse vocale :" },
            { JP, "テキスト読み上げのセットアップ方法を選択してください:" },
        },
        ["Local TTS"] = new() { { DE, "Lokale TTS" }, { FR, "TTS local" }, { JP, "ローカルTTS" } },
        ["Runs on your GPU — best quality, requires ~20GB disk space"] = new()
        {
            { DE, "Läuft auf Ihrer GPU — beste Qualität, benötigt ~20 GB Speicherplatz" },
            { FR, "Fonctionne sur votre GPU — meilleure qualité, nécessite ~20 Go d'espace" },
            { JP, "GPUで実行 — 最高品質、約20GBのディスク容量が必要" },
        },
        ["Local TTS\nRuns on your GPU — best quality, requires ~20GB disk space"] = new()
        {
            { DE, "Lokale TTS\nLäuft auf Ihrer GPU — beste Qualität, benötigt ~20 GB Speicherplatz" },
            { FR, "TTS local\nFonctionne sur votre GPU — meilleure qualité, nécessite ~20 Go d'espace" },
            { JP, "ローカルTTS\nGPUで実行 — 最高品質、約20GBのディスク容量が必要" },
        },
        ["Remote Server"] = new() { { DE, "Remote-Server" }, { FR, "Serveur distant" }, { JP, "リモートサーバー" } },
        ["Connect to a server running Alltalk (yours or someone else's)"] = new()
        {
            { DE, "Verbindung zu einem Server mit Alltalk herstellen (eigener oder fremder)" },
            { FR, "Connexion à un serveur Alltalk (le vôtre ou celui d'un autre)" },
            { JP, "Alltalkを実行しているサーバーに接続（自分または他人のサーバー）" },
        },
        ["Remote Server\nConnect to a server running Alltalk (yours or someone else's)"] = new()
        {
            { DE, "Remote-Server\nVerbindung zu einem Server mit Alltalk herstellen (eigener oder fremder)" },
            { FR, "Serveur distant\nConnexion à un serveur Alltalk (le vôtre ou celui d'un autre)" },
            { JP, "リモートサーバー\nAlltalkを実行しているサーバーに接続（自分または他人のサーバー）" },
        },
        ["Audio Files Only"] = new() { { DE, "Nur Audiodateien" }, { FR, "Fichiers audio uniquement" }, { JP, "音声ファイルのみ" } },
        ["No generation — use pre-made audio from friends or Google Drive"] = new()
        {
            { DE, "Keine Generierung — verwenden Sie vorgefertigte Audiodateien von Freunden oder Google Drive" },
            { FR, "Pas de génération — utilisez des fichiers audio préparés par des amis ou Google Drive" },
            { JP, "生成なし — 友人やGoogle Driveから既製の音声ファイルを使用" },
        },
        ["Audio Files Only\nNo generation — use pre-made audio from friends or Google Drive"] = new()
        {
            { DE, "Nur Audiodateien\nKeine Generierung — verwenden Sie vorgefertigte Audiodateien von Freunden oder Google Drive" },
            { FR, "Fichiers audio uniquement\nPas de génération — utilisez des fichiers audio préparés par des amis ou Google Drive" },
            { JP, "音声ファイルのみ\n生成なし — 友人やGoogle Driveから既製の音声ファイルを使用" },
        },
        ["You will need to get audio files from a friend or via Google Drive."] = new()
        {
            { DE, "Sie müssen Audiodateien von einem Freund oder über Google Drive besorgen." },
            { FR, "Vous devrez obtenir les fichiers audio d'un ami ou via Google Drive." },
            { JP, "友人またはGoogle Driveから音声ファイルを入手する必要があります。" },
        },
        ["Local audio directory (where audio files will be stored):"] = new()
        {
            { DE, "Lokales Audioverzeichnis (wo Audiodateien gespeichert werden):" },
            { FR, "Dossier audio local (où les fichiers audio seront stockés) :" },
            { JP, "ローカル音声ディレクトリ（音声ファイルの保存先）:" },
        },
        ["No audio will be generated. Use pre-made audio from friends or Google Drive."] = new()
        {
            { DE, "Es wird kein Audio generiert. Verwenden Sie vorgefertigte Audiodateien von Freunden oder Google Drive." },
            { FR, "Aucun audio ne sera généré. Utilisez des fichiers audio préparés par des amis ou Google Drive." },
            { JP, "音声は生成されません。友人やGoogle Driveから既製の音声ファイルを使用してください。" },
        },
        ["Download from Google Drive"] = new() { { DE, "Von Google Drive herunterladen" }, { FR, "Télécharger depuis Google Drive" }, { JP, "Google Driveからダウンロード" } },
        ["Local audio directory"] = new() { { DE, "Lokales Audioverzeichnis" }, { FR, "Dossier audio local" }, { JP, "ローカル音声ディレクトリ" } },
        ["You're all set! Press the button below to start using Echokraut."] = new()
        {
            { DE, "Alles bereit! Drücken Sie den Button unten, um Echokraut zu verwenden." },
            { FR, "Vous êtes prêt ! Appuyez sur le bouton ci-dessous pour commencer." },
            { JP, "準備完了！下のボタンを押してEchokrautの使用を開始してください。" },
        },
        ["Use /ek in chat to open the full configuration window at any time."] = new()
        {
            { DE, "Verwenden Sie /ek im Chat, um jederzeit das Konfigurationsfenster zu öffnen." },
            { FR, "Utilisez /ek dans le chat pour ouvrir la fenêtre de configuration à tout moment." },
            { JP, "チャットで /ek と入力すると、いつでも設定ウィンドウを開けます。" },
        },
        ["Use /ek in chat to open the full configuration window."] = new()
        {
            { DE, "Verwenden Sie /ek im Chat, um das Konfigurationsfenster zu öffnen." },
            { FR, "Utilisez /ek dans le chat pour ouvrir la fenêtre de configuration." },
            { JP, "チャットで /ek と入力すると設定ウィンドウを開けます。" },
        },
        ["You're all set!"] = new() { { DE, "Alles bereit!" }, { FR, "Vous êtes prêt !" }, { JP, "準備完了！" } },

        // ── Common buttons ────────────────────────────────────────────────
        ["Back"] = new() { { DE, "Zurück" }, { FR, "Retour" }, { JP, "戻る" } },
        ["Next"] = new() { { DE, "Weiter" }, { FR, "Suivant" }, { JP, "次へ" } },
        ["I Understand"] = new() { { DE, "Verstanden" }, { FR, "J'ai compris" }, { JP, "了解" } },
        ["Play"] = new() { { DE, "Abspielen" }, { FR, "Lecture" }, { JP, "再生" } },
        ["Pause"] = new() { { DE, "Pause" }, { FR, "Pause" }, { JP, "一時停止" } },
        ["Mute"] = new() { { DE, "Stumm" }, { FR, "Muet" }, { JP, "ミュート" } },
        ["Add"] = new() { { DE, "Hinzufügen" }, { FR, "Ajouter" }, { JP, "追加" } },
        ["Delete"] = new() { { DE, "Löschen" }, { FR, "Supprimer" }, { JP, "削除" } },
        ["Clear logs"] = new() { { DE, "Protokoll leeren" }, { FR, "Effacer les journaux" }, { JP, "ログをクリア" } },
        ["Clear mapped NPCs"] = new() { { DE, "Zugeordnete NPCs löschen" }, { FR, "Effacer les PNJ associés" }, { JP, "マッピング済みNPCをクリア" } },
        ["Clear mapped players"] = new() { { DE, "Zugeordnete Spieler löschen" }, { FR, "Effacer les joueurs associés" }, { JP, "マッピング済みプレイヤーをクリア" } },
        ["Clear mapped bubbles"] = new() { { DE, "Zugeordnete Sprechblasen löschen" }, { FR, "Effacer les bulles associées" }, { JP, "マッピング済み吹き出しをクリア" } },
        ["Confirm clear NPCs!"] = new() { { DE, "NPCs löschen bestätigen!" }, { FR, "Confirmer l'effacement des PNJ !" }, { JP, "NPCクリアを確認！" } },
        ["Confirm clear players!"] = new() { { DE, "Spieler löschen bestätigen!" }, { FR, "Confirmer l'effacement des joueurs !" }, { JP, "プレイヤークリアを確認！" } },
        ["Confirm clear bubbles!"] = new() { { DE, "Sprechblasen löschen bestätigen!" }, { FR, "Confirmer l'effacement des bulles !" }, { JP, "吹き出しクリアを確認！" } },
        ["Reload remote mappings"] = new() { { DE, "Remote-Zuordnungen neu laden" }, { FR, "Recharger les associations distantes" }, { JP, "リモートマッピングを再読み込み" } },
        ["Join discord server"] = new() { { DE, "Discord-Server beitreten" }, { FR, "Rejoindre le serveur Discord" }, { JP, "Discordサーバーに参加" } },
        ["Alltalk Github"] = new() { { DE, "Alltalk GitHub" }, { FR, "Alltalk GitHub" }, { JP, "Alltalk GitHub" } },
        ["Configure"] = new() { { DE, "Konfigurieren" }, { FR, "Configurer" }, { JP, "設定" } },

        // ── Dialog controls ───────────────────────────────────────────────
        ["Resume dialogue"] = new() { { DE, "Dialog fortsetzen" }, { FR, "Reprendre le dialogue" }, { JP, "ダイアログを再開" } },
        ["Pause dialogue"] = new() { { DE, "Dialog pausieren" }, { FR, "Mettre en pause le dialogue" }, { JP, "ダイアログを一時停止" } },
        ["Replay dialogue"] = new() { { DE, "Dialog wiederholen" }, { FR, "Rejouer le dialogue" }, { JP, "ダイアログをリプレイ" } },
        ["Stop dialogue"] = new() { { DE, "Dialog stoppen" }, { FR, "Arrêter le dialogue" }, { JP, "ダイアログを停止" } },
        ["Mute dialogue"] = new() { { DE, "Dialog stummschalten" }, { FR, "Couper le dialogue" }, { JP, "ダイアログをミュート" } },
        ["Unmute dialogue"] = new() { { DE, "Stummschaltung aufheben" }, { FR, "Réactiver le dialogue" }, { JP, "ミュート解除" } },
        ["Auto-advance"] = new() { { DE, "Auto-Weiter" }, { FR, "Avance auto" }, { JP, "自動進行" } },

        // ── Voice Selection ───────────────────────────────────────────────
        ["Search..."] = new() { { DE, "Suchen..." }, { FR, "Rechercher..." }, { JP, "検索..." } },
        ["Filter"] = new() { { DE, "Filter" }, { FR, "Filtre" }, { JP, "フィルター" } },
        ["Gender"] = new() { { DE, "Geschlecht" }, { FR, "Genre" }, { JP, "性別" } },
        ["Race"] = new() { { DE, "Volk" }, { FR, "Race" }, { JP, "種族" } },
        ["Name"] = new() { { DE, "Name" }, { FR, "Nom" }, { JP, "名前" } },
        ["Voice"] = new() { { DE, "Stimme" }, { FR, "Voix" }, { JP, "ボイス" } },
        ["Volume"] = new() { { DE, "Lautstärke" }, { FR, "Volume" }, { JP, "音量" } },
        ["Lock"] = new() { { DE, "Sperren" }, { FR, "Verrouiller" }, { JP, "ロック" } },
        ["Use"] = new() { { DE, "Verwenden" }, { FR, "Utiliser" }, { JP, "使用" } },
        ["En"] = new() { { DE, "An" }, { FR, "Act." }, { JP, "有効" } },
        ["Voice Name"] = new() { { DE, "Stimmenname" }, { FR, "Nom de la voix" }, { JP, "ボイス名" } },
        ["Note"] = new() { { DE, "Notiz" }, { FR, "Note" }, { JP, "メモ" } },
        ["No entries found."] = new() { { DE, "Keine Einträge gefunden." }, { FR, "Aucune entrée trouvée." }, { JP, "エントリが見つかりません。" } },
        ["No voices configured."] = new() { { DE, "Keine Stimmen konfiguriert." }, { FR, "Aucune voix configurée." }, { JP, "ボイスが設定されていません。" } },

        // ── Phonetics ─────────────────────────────────────────────────────
        ["Original"] = new() { { DE, "Original" }, { FR, "Original" }, { JP, "原文" } },
        ["Corrected"] = new() { { DE, "Korrigiert" }, { FR, "Corrigé" }, { JP, "修正後" } },
        ["New original"] = new() { { DE, "Neues Original" }, { FR, "Nouvel original" }, { JP, "新しい原文" } },
        ["New corrected"] = new() { { DE, "Neue Korrektur" }, { FR, "Nouvelle correction" }, { JP, "新しい修正" } },
        ["No phonetic corrections found."] = new() { { DE, "Keine phonetischen Korrekturen gefunden." }, { FR, "Aucune correction phonétique trouvée." }, { JP, "音声修正が見つかりません。" } },

        // ── Logs ──────────────────────────────────────────────────────────
        ["Show debug logs"] = new() { { DE, "Debug-Protokoll anzeigen" }, { FR, "Afficher les journaux de débogage" }, { JP, "デバッグログを表示" } },
        ["Show error logs"] = new() { { DE, "Fehlerprotokoll anzeigen" }, { FR, "Afficher les journaux d'erreurs" }, { JP, "エラーログを表示" } },
        ["Show ID 0 entries"] = new() { { DE, "ID-0-Einträge anzeigen" }, { FR, "Afficher les entrées ID 0" }, { JP, "ID 0のエントリを表示" } },
        ["No log entries."] = new() { { DE, "Keine Protokolleinträge." }, { FR, "Aucune entrée de journal." }, { JP, "ログエントリがありません。" } },

        // ── Log tab names ─────────────────────────────────────────────────
        ["Player choice in cutscenes"] = new() { { DE, "Spielerauswahl in Zwischensequenzen" }, { FR, "Choix du joueur en cinématique" }, { JP, "カットシーンでの選択肢" } },
        ["Player choice"] = new() { { DE, "Spielerauswahl" }, { FR, "Choix du joueur" }, { JP, "選択肢" } },

        // ── Collapsible toggle prefixes ───────────────────────────────────
        ["[+] Advanced Filters"] = new() { { DE, "[+] Erweiterte Filter" }, { FR, "[+] Filtres avancés" }, { JP, "[+] 詳細フィルター" } },
        ["[-] Advanced Filters"] = new() { { DE, "[-] Erweiterte Filter" }, { FR, "[-] Filtres avancés" }, { JP, "[-] 詳細フィルター" } },
        ["[+] Advanced Options"] = new() { { DE, "[+] Erweiterte Optionen" }, { FR, "[+] Options avancées" }, { JP, "[+] 詳細オプション" } },
        ["[-] Advanced Options"] = new() { { DE, "[-] Erweiterte Optionen" }, { FR, "[-] Options avancées" }, { JP, "[-] 詳細オプション" } },

        // ── Misc ──────────────────────────────────────────────────────────
        ["Configuration"] = new() { { DE, "Konfiguration" }, { FR, "Configuration" }, { JP, "設定" } },
        ["First Time Setup"] = new() { { DE, "Ersteinrichtung" }, { FR, "Configuration initiale" }, { JP, "初回セットアップ" } },
        ["Select a directory via dialog."] = new() { { DE, "Verzeichnis über Dialog auswählen." }, { FR, "Sélectionner un dossier via le dialogue." }, { JP, "ダイアログでフォルダを選択。" } },
        ["Choose audio files directory"] = new() { { DE, "Audiodateien-Verzeichnis wählen" }, { FR, "Choisir le dossier des fichiers audio" }, { JP, "音声ファイルディレクトリを選択" } },
        ["Choose alltalk instance directory"] = new() { { DE, "Alltalk-Instanzverzeichnis wählen" }, { FR, "Choisir le dossier de l'instance Alltalk" }, { JP, "Alltalkインスタンスディレクトリを選択" } },
        ["Test Connection"] = new() { { DE, "Verbindung testen" }, { FR, "Tester la connexion" }, { JP, "接続テスト" } },
        ["Connection test result:"] = new() { { DE, "Verbindungstest-Ergebnis:" }, { FR, "Résultat du test de connexion :" }, { JP, "接続テスト結果:" } },

        // ── Additional keys (from ConfigWindow / ImGui) ──────────────────
        ["Add phonetic correction"] = new() { { DE, "Phonetische Korrektur hinzufügen" }, { FR, "Ajouter une correction phonétique" }, { JP, "音声修正を追加" } },
        ["Remove phonetic correction"] = new() { { DE, "Phonetische Korrektur entfernen" }, { FR, "Supprimer la correction phonétique" }, { JP, "音声修正を削除" } },
        ["All"] = new() { { DE, "Alle" }, { FR, "Tous" }, { JP, "全て" } },
        ["Always jump to bottom"] = new() { { DE, "Immer nach unten springen" }, { FR, "Toujours aller en bas" }, { JP, "常に最下部に移動" } },
        ["Base Url"] = new() { { DE, "Basis-URL" }, { FR, "URL de base" }, { JP, "ベースURL" } },
        ["Child Voice"] = new() { { DE, "Kinderstimme" }, { FR, "Voix d'enfant" }, { JP, "子供の声" } },
        ["Click again to confirm deletion!"] = new() { { DE, "Zum Bestätigen erneut klicken!" }, { FR, "Cliquez à nouveau pour confirmer !" }, { JP, "もう一度クリックして確認！" } },
        ["Custom model URL"] = new() { { DE, "Benutzerdefinierte Modell-URL" }, { FR, "URL du modèle personnalisé" }, { JP, "カスタムモデルURL" } },
        ["Custom voices URL"] = new() { { DE, "Benutzerdefinierte Stimmen-URL" }, { FR, "URL des voix personnalisées" }, { JP, "カスタムボイスURL" } },
        ["Default Voice:"] = new() { { DE, "Standardstimme:" }, { FR, "Voix par défaut :" }, { JP, "デフォルトボイス:" } },
        ["DetectLanguage.com"] = new() { { DE, "DetectLanguage.com" }, { FR, "DetectLanguage.com" }, { JP, "DetectLanguage.com" } },
        ["First time using Echokraut"] = new() { { DE, "Erstmalige Nutzung von Echokraut" }, { FR, "Première utilisation d'Echokraut" }, { JP, "Echokrautの初回利用" } },
        ["Genders"] = new() { { DE, "Geschlechter" }, { FR, "Genres" }, { JP, "性別" } },
        ["ID"] = new() { { DE, "ID" }, { FR, "ID" }, { JP, "ID" } },
        ["Log:"] = new() { { DE, "Protokoll:" }, { FR, "Journal :" }, { JP, "ログ:" } },
        ["Message"] = new() { { DE, "Nachricht" }, { FR, "Message" }, { JP, "メッセージ" } },
        ["Method"] = new() { { DE, "Methode" }, { FR, "Méthode" }, { JP, "メソッド" } },
        ["None"] = new() { { DE, "Keine" }, { FR, "Aucun" }, { JP, "なし" } },
        ["Options"] = new() { { DE, "Optionen" }, { FR, "Options" }, { JP, "オプション" } },
        ["Options:"] = new() { { DE, "Optionen:" }, { FR, "Options :" }, { JP, "オプション:" } },
        ["Races"] = new() { { DE, "Völker" }, { FR, "Races" }, { JP, "種族" } },
        ["Random NPC"] = new() { { DE, "Zufälliger NPC" }, { FR, "PNJ aléatoire" }, { JP, "ランダムNPC" } },
        ["Reset"] = new() { { DE, "Zurücksetzen" }, { FR, "Réinitialiser" }, { JP, "リセット" } },
        ["Select Backend"] = new() { { DE, "Backend auswählen" }, { FR, "Sélectionner le backend" }, { JP, "バックエンドを選択" } },
        ["Stop Voice"] = new() { { DE, "Stimme stoppen" }, { FR, "Arrêter la voix" }, { JP, "ボイスを停止" } },
        ["Test Voice"] = new() { { DE, "Stimme testen" }, { FR, "Tester la voix" }, { JP, "ボイスをテスト" } },
        ["Timestamp"] = new() { { DE, "Zeitstempel" }, { FR, "Horodatage" }, { JP, "タイムスタンプ" } },
        ["Voice NPC bubbles in cities"] = new() { { DE, "NPC-Sprechblasen in Städten vertonen" }, { FR, "Bulles PNJ vocales en ville" }, { JP, "街中のNPC吹き出しをボイス化" } },
        ["Will remove all local saved audio files for this character"] = new()
        {
            { DE, "Alle lokal gespeicherten Audiodateien für diesen Charakter werden gelöscht" },
            { FR, "Tous les fichiers audio locaux de ce personnage seront supprimés" },
            { JP, "このキャラクターのローカル保存済み音声ファイルがすべて削除されます" },
        },
        ["Reset genders"] = new() { { DE, "Geschlechter zurücksetzen" }, { FR, "Réinitialiser les genres" }, { JP, "性別をリセット" } },
        ["Reset races"] = new() { { DE, "Völker zurücksetzen" }, { FR, "Réinitialiser les races" }, { JP, "種族をリセット" } },

        // ── Voice Config Window ──────────────────────────────────────────
        ["Use as random NPC voice"] = new() { { DE, "Als zufällige NPC-Stimme verwenden" }, { FR, "Utiliser comme voix PNJ aléatoire" }, { JP, "ランダムNPCボイスとして使用" } },
        ["Child voice"] = new() { { DE, "Kinderstimme" }, { FR, "Voix d'enfant" }, { JP, "子供の声" } },
        ["Allowed genders"] = new() { { DE, "Erlaubte Geschlechter" }, { FR, "Genres autorisés" }, { JP, "許可する性別" } },
        ["Allowed races"] = new() { { DE, "Erlaubte Völker" }, { FR, "Races autorisées" }, { JP, "許可する種族" } },

        // ── Genders ──────────────────────────────────────────────────────
        ["None"] = new() { { DE, "Keine" }, { FR, "Aucun" }, { JP, "なし" } },
        ["Male"] = new() { { DE, "Männlich" }, { FR, "Masculin" }, { JP, "男性" } },
        ["Female"] = new() { { DE, "Weiblich" }, { FR, "Féminin" }, { JP, "女性" } },

        // ── FFXIV Races (official localized names) ───────────────────────
        // Playable races
        ["Unknown"] = new() { { DE, "Unbekannt" }, { FR, "Inconnu" }, { JP, "不明" } },
        ["Hyur"] = new() { { DE, "Hyuran" }, { FR, "Hyuran" }, { JP, "ヒューラン" } },
        ["Elezen"] = new() { { DE, "Elezen" }, { FR, "Élézen" }, { JP, "エレゼン" } },
        ["Miqote"] = new() { { DE, "Miqo'te" }, { FR, "Miqo'te" }, { JP, "ミコッテ" } },
        ["Roegadyn"] = new() { { DE, "Roegadyn" }, { FR, "Roegadyn" }, { JP, "ルガディン" } },
        ["Lalafell"] = new() { { DE, "Lalafell" }, { FR, "Lalafell" }, { JP, "ララフェル" } },
        ["Viera"] = new() { { DE, "Viera" }, { FR, "Viéra" }, { JP, "ヴィエラ" } },
        ["AuRa"] = new() { { DE, "Au Ra" }, { FR, "Au Ra" }, { JP, "アウラ" } },
        ["Hrothgar"] = new() { { DE, "Hrothgar" }, { FR, "Hrothgar" }, { JP, "ロスガル" } },
        // Beast tribes & NPCs
        ["Amaljaa"] = new() { { DE, "Amalj'aa" }, { FR, "Amalj'aa" }, { JP, "アマルジャ" } },
        ["Ixal"] = new() { { DE, "Ixal" }, { FR, "Ixal" }, { JP, "イクサル" } },
        ["Sylph"] = new() { { DE, "Sylphe" }, { FR, "Sylphe" }, { JP, "シルフ" } },
        ["Goblin"] = new() { { DE, "Goblin" }, { FR, "Gobelin" }, { JP, "ゴブリン" } },
        ["Moogle"] = new() { { DE, "Mogry" }, { FR, "Mog" }, { JP, "モーグリ" } },
        ["MamoolJa"] = new() { { DE, "Mamool Ja" }, { FR, "Mamool Ja" }, { JP, "マムージャ" } },
        ["Qiqirn"] = new() { { DE, "Qiqirn" }, { FR, "Qiqirn" }, { JP, "キキルン" } },
        ["VanuVanu"] = new() { { DE, "Vanu Vanu" }, { FR, "Vanu Vanu" }, { JP, "バヌバヌ" } },
        ["Kojin"] = new() { { DE, "Kojin" }, { FR, "Kojin" }, { JP, "コウジン" } },
        ["Ananta"] = new() { { DE, "Ananta" }, { FR, "Ananta" }, { JP, "アナンタ" } },
        ["Lupin"] = new() { { DE, "Lupin" }, { FR, "Lupin" }, { JP, "ルピン" } },
        ["Arkasodara"] = new() { { DE, "Arkasodara" }, { FR, "Arkasodara" }, { JP, "アルカソーダラ" } },
        ["NuMou"] = new() { { DE, "Nu Mou" }, { FR, "Nu Mou" }, { JP, "ヌ・モウ" } },
        ["Pixie"] = new() { { DE, "Pixie" }, { FR, "Pixie" }, { JP, "ピクシー" } },
        ["Loporrit"] = new() { { DE, "Loporrit" }, { FR, "Loporrit" }, { JP, "ロポリット" } },
        ["Frog"] = new() { { DE, "Frosch" }, { FR, "Grenouille" }, { JP, "カエル" } },
        ["Ea"] = new() { { DE, "Ea" }, { FR, "Éa" }, { JP, "エア" } },
        ["YokHuy"] = new() { { DE, "Yok Huy" }, { FR, "Yok Huy" }, { JP, "ヨカ・フイ" } },
        ["Endless"] = new() { { DE, "Endlos" }, { FR, "Éternel" }, { JP, "エンドレス" } },
        ["Sahagin"] = new() { { DE, "Sahagin" }, { FR, "Sahuagin" }, { JP, "サハギン" } },
        ["Kobold"] = new() { { DE, "Kobold" }, { FR, "Kobold" }, { JP, "コボルド" } },
        ["Gnath"] = new() { { DE, "Gnath" }, { FR, "Gnath" }, { JP, "グナース" } },
        ["Namazu"] = new() { { DE, "Namazu" }, { FR, "Namazu" }, { JP, "ナマズオ" } },
        ["Omicron"] = new() { { DE, "Omikron" }, { FR, "Omicron" }, { JP, "オミクロン" } },
    };
}
