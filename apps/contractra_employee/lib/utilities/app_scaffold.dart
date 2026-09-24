import 'package:flutter/material.dart';

import 'responsive_body.dart';

/// Project-level [Scaffold] wrapper with an optional [ResponsiveBody].
/// Kept intentionally small at scaffolding time; extended only when a
/// concrete screen needs an option this doesn't forward yet.
class AppScaffold extends StatelessWidget {
  const AppScaffold({
    super.key,
    this.appBar,
    this.body,
    this.floatingActionButton,
    this.bottomNavigationBar,
    this.drawer,
    this.backgroundColor,
    this.enableResponsiveBody = true,
  });

  final PreferredSizeWidget? appBar;
  final Widget? body;
  final Widget? floatingActionButton;
  final Widget? bottomNavigationBar;
  final Widget? drawer;
  final Color? backgroundColor;
  final bool enableResponsiveBody;

  @override
  Widget build(BuildContext context) {
    final Widget? scaffoldBody = body == null
        ? null
        : enableResponsiveBody
            ? ResponsiveBody(child: body!)
            : body;

    return Scaffold(
      appBar: appBar,
      body: scaffoldBody,
      floatingActionButton: floatingActionButton,
      bottomNavigationBar: bottomNavigationBar,
      drawer: drawer,
      backgroundColor: backgroundColor,
    );
  }
}
