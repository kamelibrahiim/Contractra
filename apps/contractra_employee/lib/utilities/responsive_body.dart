import 'package:flutter/material.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';

/// Scales [child] down when the viewport is too small to prevent overflow.
/// Used automatically by [AppScaffold]; disable for screens that manage
/// their own scroll (ListView/GridView) via [AppScaffold.enableResponsiveBody].
class ResponsiveBody extends StatelessWidget {
  const ResponsiveBody({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        return Center(
          child: FittedBox(
            fit: BoxFit.scaleDown,
            child: Padding(
              padding: EdgeInsets.symmetric(horizontal: 16.w, vertical: 16.h),
              child: child,
            ),
          ),
        );
      },
    );
  }
}
