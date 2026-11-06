// The weekly client check's emulator walk (core #2142, item 3): the official PeerTube app, built as its debug variant and
// driven on an Android emulator through its own screens: the server is added as a person adds it, the seeded clip is
// opened, and playback is watched to advance. Copied into the app's integration_test/ directory at its newest release
// tag and run there with `flutter test … -d <emulator>`, so the widgets, the strings and the player are the app's own,
// never a copy kept here.
//
// The app speaks TLS to the host it is given; the run installed the check edge's authority into the emulator's system
// trust store first (emulator/trust-edge.sh), which the app's HTTP client and the platform's player both read. Every wait
// here polls in real time: the binding runs live, the network is real, and nothing settles on its own while a list
// shimmers. A wait that runs out names where the walk stood and what the screen said, so a red run reads without a
// screenshot.
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:peertube/features/platform/platform.dart';
import 'package:peertube/features/video/player/video_player_providers.dart';
import 'package:peertube/features/video/video_list/video_miniature.dart';
import 'package:peertube/l10n/app_localizations.dart';
import 'package:peertube/main.dart' as app;
import 'package:peertube/routes/explore/add_platform_screen.dart';
import 'package:peertube/routes/video/video_screen.dart';
import 'package:video_player/video_player.dart';

/// The video host, as a person types it. A define, because the device sees none of the runner's environment.
const String host = String.fromEnvironment('ALCIFRA_CHECK_HOST', defaultValue: 'tube.test');

/// The seeded clip's name (seed.sh).
const String clip = String.fromEnvironment('ALCIFRA_CHECK_CLIP', defaultValue: 'Test pattern');

const Duration step = Duration(milliseconds: 500);
const Duration network = Duration(seconds: 90);

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('the app adds the server, opens the clip, and playback advances', (tester) async {
    await app.main();
    await tester.pump(const Duration(seconds: 3));
    final l10n = await AppLocalizations.delegate.load(const Locale('en'));

    // Onboarding: the second button goes straight to the platform list.
    await tap(tester, find.text(l10n.iKnowWhereToGo), 'the onboarding screen');

    // The platform list: its toolbar's + opens the add screen; the host goes in as typed, Connect asks the server.
    await tap(tester, iconLabelled(l10n.addAPlatform), 'the platform list');
    await waitFor(tester, find.byType(AddPlatformScreen), 'the add-platform screen');
    await tester.enterText(find.byType(TextFormField), host);
    await tester.pump(step);
    await tap(tester, find.text(l10n.connect), 'the add-platform screen');

    // The app reads the version and the config, announces the switch (confirmed here), lands on the host's home, and
    // offers a login once (closed here: the walk browses as a guest). Then the clip is in the list.
    final confirm = find.descendant(of: find.byType(PlatformDialog), matching: find.text(l10n.confirm));
    final closeLogin = find.descendant(of: find.byType(AlertDialog), matching: iconLabelled(l10n.cancel));
    await waitFor(tester, find.text(clip), 'the platform home with the seeded clip', timeout: network, meanwhile: () async {
      await tapIfPresent(tester, confirm);
      await tapIfPresent(tester, closeLogin);
    });
    // The clip is listed twice (a horizontal shelf and the main list); the first match may sit under a header and take
    // no tap. The tile around the last one is scrolled into view and its own gesture target tapped; a tap that opens no
    // video screen within a while is tried once more on the other listing.
    final container = ProviderScope.containerOf(tester.element(find.byType(MaterialApp).first), listen: false);
    for (final text in [find.text(clip).last, find.text(clip).first]) {
      final tile = find.ancestor(of: text, matching: find.byType(VideoMiniature)).first;
      await tester.ensureVisible(tile);
      await tester.pump(step);
      final target = find.descendant(of: tile, matching: find.byType(GestureDetector)).first;
      await tester.tap(target, warnIfMissed: false);
      await tester.pump(step);
      // Opened: the app's player controller exists for the clip (the video route's widget type is the app's own business).
      final opened = await pollOrNull<bool>(tester, timeout: const Duration(seconds: 20),
          () => container.read(myVideoPlayerControllerProvider) == null && find.byType(VideoScreen).evaluate().isEmpty ? null : true);
      debugPrint('walk: tapped the clip; opened: ${opened ?? false}');
      if (opened ?? false) {
        break;
      }
    }

    // Playback, read from the app's own controller through its provider: the position must move while it plays.
    final controller = await poll<VideoPlayerController>(tester, 'an initialised player for the clip', timeout: network, () {
      final current = container.read(myVideoPlayerControllerProvider);
      return current != null && current.value.isInitialized ? current : null;
    }, progress: () {
      final current = container.read(myVideoPlayerControllerProvider);
      debugPrint('walk: video screens ${find.byType(VideoScreen).evaluate().length}, controller ${current == null ? 'none' : current.value}');
    });
    expect(controller.value.hasError, isFalse, reason: controller.value.errorDescription);
    final player = container.read(myVideoPlayerControllerProvider.notifier);
    if (controller.value.position > const Duration(seconds: 5)) {
      await player.seekTo(Duration.zero, notifyWatching: false);
    }
    if (!controller.value.isPlaying) {
      await player.play();
    }
    final before = controller.value.position;
    await tester.pump(const Duration(seconds: 5));
    final after = controller.value.position;
    debugPrint('walk: position $before → $after, playing ${controller.value.isPlaying}, duration ${controller.value.duration}');
    expect(controller.value.hasError, isFalse, reason: controller.value.errorDescription);
    expect(after - before, greaterThanOrEqualTo(const Duration(seconds: 2)), reason: 'playback did not advance');

    // The walk is proven. What follows is the app being torn down under a test harness it was not written for: its
    // providers are disposed while their screens' post-frame callbacks still run, which Riverpod reports as
    // UnmountedRefException. Playback is stopped first and the player's screen left, then those teardown reports are
    // logged rather than counted, so a green walk is not called red by the harness. Anything else still fails the test.
    await player.pause();
    await tester.pageBack();
    await tester.pump(const Duration(seconds: 2));
    final previousOnError = FlutterError.onError;
    FlutterError.onError = (details) {
      if (details.exception.runtimeType.toString() == 'UnmountedRefException') {
        debugPrint('walk: the app reported a disposed provider while being torn down (ignored): ${details.exception}');
        return;
      }
      previousOnError?.call(details);
    };
  });
}

/// An icon the app labels for assistive technology (its toolbar buttons carry no text).
Finder iconLabelled(String label) =>
    find.byWidgetPredicate((widget) => widget is Icon && widget.semanticLabel == label, description: 'an icon labelled "$label"');

/// Pumps until [finder] matches, running [meanwhile] between pumps for the dialogs that may stand in the way.
Future<Finder> waitFor(
  WidgetTester tester,
  Finder finder,
  String where, {
  Duration timeout = const Duration(seconds: 30),
  Future<void> Function()? meanwhile,
}) async {
  final deadline = DateTime.now().add(timeout);
  while (DateTime.now().isBefore(deadline)) {
    await tester.pump(step);
    if (finder.evaluate().isNotEmpty) return finder;
    if (meanwhile != null) await meanwhile();
  }
  fail('$where did not show $finder within ${timeout.inSeconds} s; the screen says: ${visibleTexts()}');
}

Future<void> tap(WidgetTester tester, Finder finder, String where, {Duration timeout = const Duration(seconds: 30)}) async {
  final found = await waitFor(tester, finder, where, timeout: timeout);
  await tester.tap(found.first);
  await tester.pump(step);
}

Future<void> tapIfPresent(WidgetTester tester, Finder finder) async {
  if (finder.evaluate().isEmpty) return;
  await tester.tap(finder.first);
  await tester.pump(step);
}

/// Pumps until [read] answers, for state the widgets do not show as text; [progress] is told every ten seconds.
Future<T> poll<T>(WidgetTester tester, String what, T? Function() read, {Duration timeout = const Duration(seconds: 30), void Function()? progress}) async =>
    await pollOrNull(tester, read, timeout: timeout, progress: progress)
        ?? fail('$what did not appear within ${timeout.inSeconds} s; the screen says: ${visibleTexts()}');

Future<T?> pollOrNull<T>(WidgetTester tester, T? Function() read, {Duration timeout = const Duration(seconds: 30), void Function()? progress}) async {
  final deadline = DateTime.now().add(timeout);
  var ticks = 0;
  while (DateTime.now().isBefore(deadline)) {
    await tester.pump(step);
    final value = read();
    if (value != null) return value;
    if (++ticks % 20 == 0) progress?.call();
  }
  return null;
}

/// What a person would read on the screen now: the context a failed wait reports.
String visibleTexts() => find
    .byType(Text)
    .evaluate()
    .map((element) => (element.widget as Text).data)
    .whereType<String>()
    .map((text) => text.trim())
    .where((text) => text.isNotEmpty)
    .take(40)
    .join(' | ');
