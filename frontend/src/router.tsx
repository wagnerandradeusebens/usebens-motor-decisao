import { createBrowserRouter } from 'react-router-dom';
import { AppLayout } from './components/AppLayout';
import { FlowListPage } from './pages/FlowListPage';
import { FlowDetailPage } from './pages/FlowDetailPage';
import { EditorPage } from './pages/EditorPage';
import { SourcesPage } from './pages/SourcesPage';
import { ExecutionsPage } from './pages/ExecutionsPage';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <AppLayout />,
    children: [
      { index: true, element: <FlowListPage /> },
      { path: 'flows/:flowId', element: <FlowDetailPage /> },
      { path: 'flows/:flowId/versions/:versionId', element: <EditorPage /> },
      { path: 'flows/:flowId/execucoes', element: <ExecutionsPage /> },
      { path: 'fontes', element: <SourcesPage /> },
    ],
  },
]);
