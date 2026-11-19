import 'package:connectivity_plus/connectivity_plus.dart';

/// Whether a connectivity change brought a network the upload can use.
bool hasNetwork(List<ConnectivityResult> results) => results.any((result) => switch (result) {
      ConnectivityResult.wifi ||
      ConnectivityResult.mobile ||
      ConnectivityResult.ethernet ||
      ConnectivityResult.vpn ||
      ConnectivityResult.other =>
        true,
      _ => false,
    });

/// One event each time Wi-Fi, mobile data or another network becomes available (also when the
/// phone moves from one Wi-Fi to another): the upload queue tries again at once.
Stream<void> networkChanges(Stream<List<ConnectivityResult>> changes) => changes.where(hasNetwork);
