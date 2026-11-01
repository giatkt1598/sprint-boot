import { Component, type ReactNode } from 'react';
import { ErrorState } from './StateViews';

/** Keeps a render failure from blanking the whole app. */
export class ErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  override state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  override render() {
    return this.state.failed ? (
      <main className="p-8">
        <ErrorState onRetry={() => window.location.reload()} />
      </main>
    ) : (
      this.props.children
    );
  }
}
